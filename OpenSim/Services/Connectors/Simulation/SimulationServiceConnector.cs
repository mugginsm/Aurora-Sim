﻿/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;

using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using log4net;
using Nini.Config;
using Aurora.Simulation.Base;

namespace OpenSim.Services.Connectors.Simulation
{
    public class SimulationServiceConnector : ISimulationService, IService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        protected LocalSimulationServiceConnector m_localBackend;
        /// <summary>
        /// These are regions that have timed out and we are not sending updates to until the (int) time passes
        /// </summary>
        protected Dictionary<string, int> m_blackListedRegions = new Dictionary<string, int>();
        
        public IScene GetScene(ulong regionHandle)
        {
            return m_localBackend.GetScene(regionHandle);
        }

        public ISimulationService GetInnerService()
        {
            return m_localBackend;
        }

        #region Agents

        protected virtual string AgentPath()
        {
            return "/agent/";
        }

        public bool CreateAgent(GridRegion destination, AgentCircuitData aCircuit, uint teleportFlags, AgentData data, out string reason)
        {
            reason = String.Empty;
            // Try local first
            if (m_localBackend.CreateAgent(destination, aCircuit, teleportFlags, data, out reason))
                return true;

            reason = String.Empty;

            string uri = MakeUri(destination, true) + aCircuit.AgentID + "/";

            try
            {
                OSDMap args = aCircuit.PackAgentCircuitData();

                args["destination_x"] = OSD.FromString(destination.RegionLocX.ToString());
                args["destination_y"] = OSD.FromString(destination.RegionLocY.ToString());
                args["destination_name"] = OSD.FromString(destination.RegionName);
                args["destination_uuid"] = OSD.FromString(destination.RegionID.ToString());
                args["teleport_flags"] = OSD.FromString(teleportFlags.ToString());
                if(data != null)
                    args["agent_data"] = data.Pack();

                OSDMap result = WebUtils.PostToService(uri, args);
                OSDMap results = WebUtils.GetOSDMap(result["_RawResult"].AsString());
                //Pull out the result and set it as the reason
                if (results == null)
                    return false;
                reason = results["reason"] != null ? results["reason"].AsString() : "";
                if (result["Success"].AsBoolean())
                {
                    try
                    {
                        OSDMap responseMap = (OSDMap)OSDParser.DeserializeJson(reason);
                    }
                    catch
                    {
                        //Not right... don't return true;
                        return false;
                    }
                    return true;
                }

                reason = result["Message"] != null ? result["Message"].AsString() : "error";
                return false;
            }
            catch (Exception e)
            {
                m_log.Warn("[REMOTE SIMULATION CONNECTOR]: CreateAgent failed with exception: " + e.ToString());
                reason = e.Message;
            }

            return false;
        }

        public bool UpdateAgent(GridRegion destination, AgentData data)
        {
            return UpdateAgent(destination, (IAgentData)data);
        }

        public bool UpdateAgent(GridRegion destination, AgentPosition data)
        {
            return UpdateAgent(destination, (IAgentData)data);
        }

        private bool UpdateAgent(GridRegion destination, IAgentData cAgentData)
        {
            // Try local first
            if (cAgentData is AgentData)
            {
                if (m_localBackend.UpdateAgent(destination, (AgentData)cAgentData))
                    return true;
            }
            else if (cAgentData is AgentPosition)
            {
                if (m_localBackend.UpdateAgent(destination, (AgentPosition)cAgentData))
                    return true;
            }

            // else do the remote thing
            if (!m_localBackend.IsLocalRegion(destination.RegionHandle))
            {
                // Eventually, we want to use a caps url instead of the agentID
                string uri = MakeUri(destination, true) + cAgentData.AgentID + "/";

                if (m_blackListedRegions.ContainsKey(uri))
                {
                    //Check against time
                    if(Util.EnvironmentTickCountSubtract(m_blackListedRegions[uri]) > 0)
                    {
                        //Still blacklisted
                        return false;
                    }
                }

                try
                {
                    OSDMap args = cAgentData.Pack();

                    args["destination_x"] = OSD.FromString(destination.RegionLocX.ToString());
                    args["destination_y"] = OSD.FromString(destination.RegionLocY.ToString());
                    args["destination_name"] = OSD.FromString(destination.RegionName);
                    args["destination_uuid"] = OSD.FromString(destination.RegionID.ToString());

                    OSDMap result = WebUtils.PutToService(uri, args);
                    if (!result["Success"].AsBoolean())
                    {
                        //add it to the blacklist
                        m_blackListedRegions[uri] = Util.EnvironmentTickCount() + 60 * 1000; //60 seconds
                        return result["Success"].AsBoolean();
                    }
                    //Clear out the blacklist if it went through
                    m_blackListedRegions.Remove(uri);

                    OSDMap innerResult = (OSDMap)result["_Result"];
                    return innerResult["Updated"].AsBoolean();
                }
                catch (Exception e)
                {
                    m_log.Warn("[REMOTE SIMULATION CONNECTOR]: UpdateAgent failed with exception: " + e.ToString());
                }

                return false;
            }

            return false;
        }

        public bool RetrieveAgent(GridRegion destination, UUID id, out IAgentData agent)
        {
            agent = null;
            // Try local first
            if (m_localBackend.RetrieveAgent(destination, id, out agent))
                return true;

            // else do the remote thing
            if (!m_localBackend.IsLocalRegion(destination.RegionHandle))
            {
                // Eventually, we want to use a caps url instead of the agentID
                string uri = MakeUri(destination, true) + id + "/" + destination.RegionID.ToString() + "/";

                try
                {
                    OSDMap result = WebUtils.GetFromService(uri);
                    if (result["Success"].AsBoolean())
                    {
                        agent = new AgentData();
                        agent.Unpack(result);
                        return true;
                    }
                }
                catch (Exception e)
                {
                    m_log.Warn("[REMOTE SIMULATION CONNECTOR]: UpdateAgent failed with exception: " + e.ToString());
                }

                return false;
            }

            return false;
        }

        public bool CloseAgent(GridRegion destination, UUID id)
        {
            // Try local first
            if (m_localBackend.CloseAgent(destination, id))
                return true;

            // else do the remote thing
            if (!m_localBackend.IsLocalRegion(destination.RegionHandle))
            {
                string uri = MakeUri(destination, true) + id + "/" + destination.RegionID.ToString() + "/";

                try
                {
                    OSDMap result = WebUtils.ServiceOSDRequest(uri, null, "DELETE", 10000);
                }
                catch (Exception e)
                {
                    m_log.WarnFormat("[REMOTE SIMULATION CONNECTOR] CloseAgent failed with exception; {0}", e.ToString());
                }

                return true;
            }

            return false;
        }

        #endregion Agents

        #region Objects

        protected virtual string ObjectPath()
        {
            return "/object/";
        }

        public bool CreateObject(GridRegion destination, ISceneObject sog)
        {
            // Try local first
            if (m_localBackend.CreateObject(destination, sog))
            {
                //m_log.Debug("[REST COMMS]: LocalBackEnd SendCreateObject succeeded");
                return true;
            }

            // else do the remote thing
            bool successful = false;
            if (!m_localBackend.IsLocalRegion(destination.RegionHandle))
            {
                string uri = MakeUri(destination, false) + sog.UUID + "/";
                //m_log.Debug("   >>> DoCreateObjectCall <<< " + uri);

                OSDMap args = new OSDMap(7);
                args["sog"] = OSD.FromString(sog.ToXml2());
                args["extra"] = OSD.FromString(sog.ExtraToXmlString());
                string state = sog.GetStateSnapshot();
                if (state.Length > 0)
                    args["state"] = OSD.FromString(state);
                // Add the input general arguments
                args["destination_x"] = OSD.FromString(destination.RegionLocX.ToString());
                args["destination_y"] = OSD.FromString(destination.RegionLocY.ToString());
                args["destination_name"] = OSD.FromString(destination.RegionName);
                args["destination_uuid"] = OSD.FromString(destination.RegionID.ToString());

                OSDMap result = WebUtils.PostToService(uri, args);
                if (bool.TryParse(result["_RawResult"], out successful))
                    return successful;
            }
            return successful;
        }

        public bool CreateObject(GridRegion destination, UUID userID, UUID itemID)
        {
            // Try local first
            if (m_localBackend.CreateObject(destination, userID, itemID))
            {
                //m_log.Debug("[REST COMMS]: LocalBackEnd SendCreateObject succeeded");
                return true;
            }

            bool successful = false;
            // else do the remote thing
            if (!m_localBackend.IsLocalRegion(destination.RegionHandle))
            {
                string uri = MakeUri(destination, false) + itemID + "/";
                //m_log.Debug("   >>> DoCreateObjectCall <<< " + uri);

                OSDMap args = new OSDMap(6);
                args["userID"] = OSD.FromUUID(userID);
                args["itemID"] = OSD.FromUUID(itemID);
                // Add the input general arguments
                args["destination_x"] = OSD.FromString(destination.RegionLocX.ToString());
                args["destination_y"] = OSD.FromString(destination.RegionLocY.ToString());
                args["destination_name"] = OSD.FromString(destination.RegionName);
                args["destination_uuid"] = OSD.FromString(destination.RegionID.ToString());

                OSDMap result = WebUtils.PostToService(uri, args);
                if(bool.TryParse(result["_RawResult"], out successful))
                    return successful;
            }
            return successful;
        }

        public void RemoveScene(IScene scene)
        {
            m_localBackend.RemoveScene(scene);
        }

        public void Init(IScene scene)
        {
            m_localBackend.Init(scene);
        }

        #endregion Objects

        #region Misc

        private string MakeUri(GridRegion destination, bool isAgent)
        {
            if (isAgent && destination.GenericMap.ContainsKey("SimulationAgent"))
                return destination.ServerURI + destination.GenericMap["SimulationAgent"].AsString();
            if (!isAgent && destination.GenericMap.ContainsKey("SimulationObject"))
                return destination.ServerURI + destination.GenericMap["SimulationObject"].AsString();
            else
                return destination.ServerURI + (isAgent ? AgentPath() : ObjectPath());
        }

        #endregion

        #region IService Member

        public virtual void Initialize(IConfigSource config, IRegistryCore registry)
        {
            IConfig handlers = config.Configs["Handlers"];
            if (handlers.GetString("SimulationHandler", "") == "SimulationServiceConnector")
            {
                registry.RegisterModuleInterface<ISimulationService>(this);
                m_localBackend = new LocalSimulationServiceConnector();
            }
        }

        public virtual void Start(IConfigSource config, IRegistryCore registry)
        {
        }

        public void FinishedStartup()
        {
        }

        #endregion
    }
}
