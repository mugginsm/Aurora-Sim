/*
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

using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using Aurora.Simulation.Base;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace OpenSim.Services.Connectors
{
    public class GridServicesConnector : IGridService, IService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private List<string> m_ServerURIs = new List<string>();
        private IRegistryCore m_registry;

        #region IGridService

        public virtual string RegisterRegion(GridRegion regionInfo, UUID SecureSessionID, out UUID SessionID)
        {
            OSDMap map = new OSDMap();
            map["Region"] = regionInfo.ToOSD();
            map["SecureSessionID"] = SecureSessionID;
            map["Method"] = "Register";

            List<string> urls = m_registry.RequestModuleInterface<IConfigurationService>().FindValueOf("RegistrationURI");
            foreach (string m_ServerURI in urls)
            {
                OSDMap result = WebUtils.PostToService(m_ServerURI + "/grid", map);
                if (result["Success"].AsBoolean())
                {
                    try
                    {
                        OSD r = OSDParser.DeserializeJson(result["_RawResult"]);
                        if (r is OSDMap)
                        {
                            OSDMap innerresult = (OSDMap)r;
                            if (innerresult["Result"].AsString() == "")
                            {
                                SessionID = innerresult["SecureSessionID"].AsUUID();
                                m_registry.RequestModuleInterface<IConfigurationService>().AddNewUrls("default", (OSDMap)innerresult["URLs"]);
                                return "";
                            }
                            else
                            {
                                SessionID = UUID.Zero;
                                return innerresult["Result"].AsString();
                            }
                        }
                    }
                    catch (Exception)//JsonException
                    {
                        m_log.Warn("[GridServiceConnector]: Exception on parsing OSDMap from server, legacy (OpenSim) server?");
                    }
                }
            } 
            SessionID = UUID.Zero;
            return OldRegisterRegion(regionInfo);
        }

        public string OldRegisterRegion(GridRegion region)
        {
            Dictionary<string, object> rinfo = region.ToKeyValuePairs();
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kvp in rinfo)
                sendData[kvp.Key] = (string)kvp.Value;

            sendData["SCOPEID"] = region.ScopeID.ToString();
            sendData["VERSIONMIN"] = ProtocolVersions.ClientProtocolVersionMin.ToString();
            sendData["VERSIONMAX"] = ProtocolVersions.ClientProtocolVersionMax.ToString();
            sendData["METHOD"] = "register";

            string reqString = WebUtils.BuildQueryString(sendData);
            // m_log.DebugFormat("[GRID CONNECTOR]: queryString = {0}", reqString);
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    string reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            reqString);
                    if (reply != string.Empty)
                    {
                        Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                        if (replyData.ContainsKey("Result") && (replyData["Result"].ToString().ToLower() == "success"))
                        {
                            return String.Empty;
                        }
                        else if (replyData.ContainsKey("Result") && (replyData["Result"].ToString().ToLower() == "failure"))
                        {
                            m_log.DebugFormat("[GRID CONNECTOR]: Registration failed: {0}", replyData["Message"].ToString());
                            return replyData["Message"].ToString();
                        }
                        else if (!replyData.ContainsKey("Result"))
                        {
                            m_log.DebugFormat("[GRID CONNECTOR]: reply data does not contain result field");
                        }
                        else
                        {
                            m_log.DebugFormat("[GRID CONNECTOR]: unexpected result {0}", replyData["Result"].ToString());
                            return "Unexpected result " + replyData["Result"].ToString();
                        }

                    }
                    else
                        m_log.DebugFormat("[GRID CONNECTOR]: RegisterRegion received null reply");
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
            }

            return "Error communicating with grid service";
        }

        public virtual string UpdateMap(GridRegion regionInfo, UUID SecureSessionID)
        {
            OSDMap map = new OSDMap();
            map["Region"] = regionInfo.ToOSD();
            map["SecureSessionID"] = SecureSessionID;
            map["Method"] = "UpdateMap";

            foreach (string m_ServerURI in m_ServerURIs)
            {
                OSDMap result = WebUtils.PostToService(m_ServerURI + "/grid", map);
                if (result["Success"].AsBoolean())
                {
                    try
                    {
                        OSDMap innerresult = (OSDMap)result["_Result"];
                        return innerresult["Result"].AsString();
                    }
                    catch
                    {
                    }
                }
            }
            return "Error communicating with grid service";
        }

        public virtual bool DeregisterRegion(UUID regionID, UUID SessionID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["REGIONID"] = regionID.ToString();
            sendData["SESSIONID"] = SessionID.ToString();

            sendData["METHOD"] = "deregister";

            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    string reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));

                    if (reply != string.Empty)
                    {
                        Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                        if ((replyData["Result"] != null) && (replyData["Result"].ToString().ToLower() == "success"))
                            return true;
                    }
                    else
                        m_log.DebugFormat("[GRID CONNECTOR]: DeregisterRegion received null reply");
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
            }

            return false;
        }

        public virtual GridRegion GetRegionByUUID(UUID scopeID, UUID regionID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["SCOPEID"] = scopeID.ToString();
            sendData["REGIONID"] = regionID.ToString();

            sendData["METHOD"] = "get_region_by_uuid";

            string reply = string.Empty;
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
                return null;
            }

            GridRegion rinfo = null;

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                if ((replyData != null) && (replyData.ContainsKey("result") && replyData["result"] != null))
                {
                    if (replyData["result"] is Dictionary<string, object>)
                        rinfo = new GridRegion((Dictionary<string, object>)replyData["result"]);
                    //else
                    //    m_log.DebugFormat("[GRID CONNECTOR]: GetRegionByUUID {0}, {1} received null response",
                    //        scopeID, regionID);
                }
                else
                    m_log.DebugFormat("[GRID CONNECTOR]: GetRegionByUUID {0}, {1} received null response: {2}",
                        scopeID, regionID, reply);
            }
            else
                m_log.DebugFormat("[GRID CONNECTOR]: GetRegionByUUID received null reply");

            return rinfo;
        }

        public virtual GridRegion GetRegionByPosition(UUID scopeID, int x, int y)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["SCOPEID"] = scopeID.ToString();
            sendData["X"] = x.ToString();
            sendData["Y"] = y.ToString();

            sendData["METHOD"] = "get_region_by_position";
            string reply = string.Empty;
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
                return null;
            }

            GridRegion rinfo = null;
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                if ((replyData != null) && (replyData["result"] != null))
                {
                    if (replyData["result"] is Dictionary<string, object>)
                        rinfo = new GridRegion((Dictionary<string, object>)replyData["result"]);
                    //else
                    //    m_log.DebugFormat("[GRID CONNECTOR]: GetRegionByPosition {0}, {1}-{2} received no region",
                    //        scopeID, x, y);
                }
                else
                    m_log.DebugFormat("[GRID CONNECTOR]: GetRegionByPosition {0}, {1}-{2} received null response",
                        scopeID, x, y);
            }
            else
                m_log.DebugFormat("[GRID CONNECTOR]: GetRegionByPosition received null reply");

            return rinfo;
        }

        public virtual GridRegion GetRegionByName(UUID scopeID, string regionName)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["SCOPEID"] = scopeID.ToString();
            sendData["NAME"] = regionName;

            sendData["METHOD"] = "get_region_by_name";
            string reply = string.Empty;
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
                return null;
            }

            GridRegion rinfo = null;
            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                if ((replyData != null) && (replyData["result"] != null))
                {
                    if (replyData["result"] is Dictionary<string, object>)
                        rinfo = new GridRegion((Dictionary<string, object>)replyData["result"]);
                }
                else
                    m_log.DebugFormat("[GRID CONNECTOR]: GetRegionByPosition {0}, {1} received null response",
                        scopeID, regionName);
            }
            else
                m_log.DebugFormat("[GRID CONNECTOR]: GetRegionByName received null reply");

            return rinfo;
        }

        public virtual List<GridRegion> GetRegionsByName(UUID scopeID, string name, int maxNumber)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["SCOPEID"] = scopeID.ToString();
            sendData["NAME"] = name;
            sendData["MAX"] = maxNumber.ToString();

            sendData["METHOD"] = "get_regions_by_name";
            List<GridRegion> rinfos = new List<GridRegion>();
            string reply = string.Empty;
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
                return rinfos;
            }

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                if (replyData != null)
                {
                    Dictionary<string, object>.ValueCollection rinfosList = replyData.Values;
                    foreach (object r in rinfosList)
                    {
                        if (r is Dictionary<string, object>)
                        {
                            GridRegion rinfo = new GridRegion((Dictionary<string, object>)r);
                            rinfos.Add(rinfo);
                        }
                    }
                }
                else
                    m_log.DebugFormat("[GRID CONNECTOR]: GetRegionsByName {0}, {1}, {2} received null response",
                        scopeID, name, maxNumber);
            }
            else
                m_log.DebugFormat("[GRID CONNECTOR]: GetRegionsByName received null reply");

            return rinfos;
        }

        public virtual List<GridRegion> GetRegionRange(UUID scopeID, int xmin, int xmax, int ymin, int ymax)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["SCOPEID"] = scopeID.ToString();
            sendData["XMIN"] = xmin.ToString();
            sendData["XMAX"] = xmax.ToString();
            sendData["YMIN"] = ymin.ToString();
            sendData["YMAX"] = ymax.ToString();

            sendData["METHOD"] = "get_region_range";

            List<GridRegion> rinfos = new List<GridRegion>();
            string reply = string.Empty;
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));

                    //m_log.DebugFormat("[GRID CONNECTOR]: reply was {0}", reply);
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
                return rinfos;
            }

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                if (replyData != null)
                {
                    Dictionary<string, object>.ValueCollection rinfosList = replyData.Values;
                    foreach (object r in rinfosList)
                    {
                        if (r is Dictionary<string, object>)
                        {
                            GridRegion rinfo = new GridRegion((Dictionary<string, object>)r);
                            rinfos.Add(rinfo);
                        }
                    }
                }
                else
                    m_log.DebugFormat("[GRID CONNECTOR]: GetRegionRange {0}, {1}-{2} {3}-{4} received null response",
                        scopeID, xmin, xmax, ymin, ymax);
            }
            else
                m_log.DebugFormat("[GRID CONNECTOR]: GetRegionRange received null reply");

            return rinfos;
        }

        public List<GridRegion> GetDefaultRegions(UUID scopeID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["SCOPEID"] = scopeID.ToString();

            sendData["METHOD"] = "get_default_regions";

            List<GridRegion> rinfos = new List<GridRegion>();
            string reply = string.Empty;
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));

                    //m_log.DebugFormat("[GRID CONNECTOR]: reply was {0}", reply);
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
                return rinfos;
            }

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                if (replyData != null)
                {
                    Dictionary<string, object>.ValueCollection rinfosList = replyData.Values;
                    foreach (object r in rinfosList)
                    {
                        if (r is Dictionary<string, object>)
                        {
                            GridRegion rinfo = new GridRegion((Dictionary<string, object>)r);
                            rinfos.Add(rinfo);
                        }
                    }
                }
                else
                    m_log.DebugFormat("[GRID CONNECTOR]: GetDefaultRegions {0} received null response",
                        scopeID);
            }
            else
                m_log.DebugFormat("[GRID CONNECTOR]: GetDefaultRegions received null reply");

            return rinfos;
        }

        public List<GridRegion> GetFallbackRegions(UUID scopeID, int x, int y)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["SCOPEID"] = scopeID.ToString();
            sendData["X"] = x.ToString();
            sendData["Y"] = y.ToString();

            sendData["METHOD"] = "get_fallback_regions";

            List<GridRegion> rinfos = new List<GridRegion>();
            string reply = string.Empty;
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));

                    //m_log.DebugFormat("[GRID CONNECTOR]: reply was {0}", reply);
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
                return rinfos;
            }

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                if (replyData != null)
                {
                    Dictionary<string, object>.ValueCollection rinfosList = replyData.Values;
                    foreach (object r in rinfosList)
                    {
                        if (r is Dictionary<string, object>)
                        {
                            GridRegion rinfo = new GridRegion((Dictionary<string, object>)r);
                            rinfos.Add(rinfo);
                        }
                    }
                }
                else
                    m_log.DebugFormat("[GRID CONNECTOR]: GetFallbackRegions {0}, {1}-{2} received null response",
                        scopeID, x, y);
            }
            else
                m_log.DebugFormat("[GRID CONNECTOR]: GetFallbackRegions received null reply");

            return rinfos;
        }

        public List<GridRegion> GetSafeRegions(UUID scopeID, int x, int y)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["SCOPEID"] = scopeID.ToString();
            sendData["X"] = x.ToString();
            sendData["Y"] = y.ToString();

            sendData["METHOD"] = "get_safe_regions";

            List<GridRegion> rinfos = new List<GridRegion>();
            string reply = string.Empty;
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));

                    //m_log.DebugFormat("[GRID CONNECTOR]: reply was {0}", reply);
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
                return rinfos;
            }

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                if (replyData != null)
                {
                    Dictionary<string, object>.ValueCollection rinfosList = replyData.Values;
                    foreach (object r in rinfosList)
                    {
                        if (r is Dictionary<string, object>)
                        {
                            GridRegion rinfo = new GridRegion((Dictionary<string, object>)r);
                            rinfos.Add(rinfo);
                        }
                    }
                }
                else
                    m_log.DebugFormat("[GRID CONNECTOR]: GetSafeRegions {0}, {1}-{2} received null response",
                        scopeID, x, y);
            }
            else
                m_log.DebugFormat("[GRID CONNECTOR]: GetSafeRegions received null reply");

            return rinfos;
        }

		public List<GridRegion> GetHyperlinks(UUID scopeID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["SCOPEID"] = scopeID.ToString();

            sendData["METHOD"] = "get_hyperlinks";

            List<GridRegion> rinfos = new List<GridRegion>();
            string reply = string.Empty;
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));

                    //m_log.DebugFormat("[GRID CONNECTOR]: reply was {0}", reply);
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
                return rinfos;
            }

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                if (replyData != null)
                {
                    Dictionary<string, object>.ValueCollection rinfosList = replyData.Values;
                    foreach (object r in rinfosList)
                    {
                        if (r is Dictionary<string, object>)
                        {
                            GridRegion rinfo = new GridRegion((Dictionary<string, object>)r);
                            rinfos.Add(rinfo);
                        }
                    }
                }
                else
                    m_log.DebugFormat("[GRID CONNECTOR]: GetHyperlinks {0} received null response",
                        scopeID);
            }
            else
                m_log.DebugFormat("[GRID CONNECTOR]: GetHyperlinks received null reply");

            return rinfos;
        }

        public virtual int GetRegionFlags(UUID scopeID, UUID regionID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["SCOPEID"] = scopeID.ToString();
            sendData["REGIONID"] = regionID.ToString();

            sendData["METHOD"] = "get_region_flags";

            string reply = string.Empty;
            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            WebUtils.BuildQueryString(sendData));
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting grid server: {0}", e.Message);
                return -1;
            }

            int flags = -1;

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                if ((replyData != null) && replyData.ContainsKey("result") && (replyData["result"] != null))
                {
                    Int32.TryParse((string)replyData["result"], out flags);
                    //else
                    //    m_log.DebugFormat("[GRID CONNECTOR]: GetRegionFlags {0}, {1} received wrong type {2}",
                    //        scopeID, regionID, replyData["result"].GetType());
                }
                else
                    m_log.DebugFormat("[GRID CONNECTOR]: GetRegionFlags {0}, {1} received null response",
                        scopeID, regionID);
            }
            else
                m_log.DebugFormat("[GRID CONNECTOR]: GetRegionFlags received null reply");

            return flags;
        }

        public multipleMapItemReply GetMapItems(ulong regionHandle, GridItemType gridItemType)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["REGIONHANDLE"] = regionHandle;
            sendData["GRIDITEMTYPE"] = (int)gridItemType;
            sendData["METHOD"] = "getmapitems";

            string reqString = WebUtils.BuildQueryString(sendData);

            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    string reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/grid",
                            reqString);
                    if (reply != string.Empty)
                    {
                        Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                        if (replyData != null)
                        {
                            multipleMapItemReply items = new multipleMapItemReply();
                            if (replyData.ContainsKey("Result") && (replyData["Result"].ToString().ToLower() == "failure"))
                                return items;

                            items = new multipleMapItemReply((replyData["MapItems"]) as Dictionary<string, object>);

                            // Success
                            return items;
                        }

                        else
                            m_log.DebugFormat("[GRID CONNECTOR]: GetMapItems {0} received null response",
                                regionHandle);
                    }
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID CONNECTOR]: Exception when contacting server: {0}", e.Message);
            }

            return null;
        }

        public virtual void SetRegionUnsafe(UUID regionID)
        {
        }

        #endregion

        #region IService Members

        public string Name
        {
            get { return GetType().Name; }
        }

        public void Initialize(IConfigSource config, IRegistryCore registry)
        {
            IConfig handlerConfig = config.Configs["Handlers"];
            if (handlerConfig.GetString("GridHandler", "") != Name)
                return;

            registry.RegisterModuleInterface<IGridService>(this);
            m_registry = registry;
        }

        public void Start(IConfigSource config, IRegistryCore registry)
        {
            m_ServerURIs = registry.RequestModuleInterface<IConfigurationService>().FindValueOf("GridServerURI");
        }

        public void FinishedStartup()
        {
        }

        #endregion
    }
}
