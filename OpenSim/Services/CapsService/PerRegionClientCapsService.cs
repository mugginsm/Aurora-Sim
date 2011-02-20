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

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using Aurora.Framework;
using OpenSim.Framework;

namespace OpenSim.Services.CapsService
{
    /// <summary>
    /// CapsHandlers is a cap handler container but also takes
    /// care of adding and removing cap handlers to and from the
    /// supplied BaseHttpServer.
    /// </summary>
    public class PerRegionClientCapsService : IRegionClientCapsService
    {
        #region Declares

        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private List<ICapsServiceConnector> m_connectors = new List<ICapsServiceConnector>();
        private bool m_disabled = true;
        private AgentCircuitData m_circuitData;
        public AgentCircuitData CircuitData
        {
            get { return m_circuitData; }
        }
        public bool Disabled
        {
            get { return m_disabled; }
            set { m_disabled = value; }
        }
        
        private ulong m_RegionHandle;
        public ulong RegionHandle
        {
            get { return m_RegionHandle; }
        }

        public int RegionX
        {
            get
            {
                int x, y;
                Util.UlongToInts(m_RegionHandle, out x, out y);
                return x;
            }
        }

        public int RegionY
        {
            get
            {
                int x, y;
                Util.UlongToInts(m_RegionHandle, out x, out y);
                return y;
            }
        }

        private Vector3 m_lastPosition;
        public Vector3 LastPosition
        {
            get { return m_lastPosition; }
            set { m_lastPosition = value; }
        }

        private string m_UrlToInform = "";
        /// <summary>
        /// An optional Url that will be called to retrieve more Caps for the client.
        /// </summary>
        public string UrlToInform
        {
            get { return m_UrlToInform; }
            set { m_UrlToInform = value; }
        }

        /// <summary>
        /// This is the /CAPS/UUID 0000/ string
        /// </summary>
        protected String m_capsUrlBase;
        
        public UUID AgentID
        {
            get { return m_clientCapsService.AgentID; }
        }

        protected bool m_isRootAgent = false;
        public bool RootAgent
        {
            get { return m_isRootAgent; }
            set { m_isRootAgent = value; }
        }

        protected IClientCapsService m_clientCapsService;
        public IClientCapsService ClientCaps
        {
            get { return m_clientCapsService; }
        }

        #endregion

        #region Properties

        public String HostUri
        {
            get { return m_clientCapsService.HostUri; }
        }

        public IRegistryCore Registry
        {
            get { return m_clientCapsService.Registry; }
        }

        protected IHttpServer Server
        {
            get { return m_clientCapsService.Server; }
        }

        /// <summary>
        /// This is the full URL to the Caps SEED request
        /// </summary>
        public String CapsUrl
        {
            get { return HostUri + m_capsUrlBase; }
        }

        #endregion

        #region Initialize

        public void Initialise(IClientCapsService clientCapsService, ulong regionHandle, string capsBase, string urlToInform, AgentCircuitData circuitData)
        {
            m_clientCapsService = clientCapsService;
            m_RegionHandle = regionHandle;
            m_circuitData = circuitData;
            AddSEEDCap(capsBase, urlToInform);

            AddCAPS();
        }

        #endregion

        #region Add/Remove Caps from the known caps OSDMap

        //X cap name to path
        protected OSDMap registeredCAPS = new OSDMap();
        //Paths to X cap
        protected OSDMap registeredCAPSPath = new OSDMap();

        public string CreateCAPS(string method, string appendedPath)
        {
            string caps = "/CAPS/" + method + "/" + UUID.Random() + appendedPath + "/";
            return caps;
        }

        protected void AddCAPS(string method, string caps)
        {
            if (method == null || caps == null)
                return;
            string CAPSPath = HostUri + caps;
            registeredCAPS[method] = CAPSPath;
            registeredCAPSPath[CAPSPath] = method;
        }

        protected void RemoveCaps(string method)
        {
            OSD CapsPath = "";
            if (!registeredCAPS.TryGetValue(method, out CapsPath))
                return;
            registeredCAPS.Remove(method);
            registeredCAPSPath.Remove(CapsPath.AsString());
        }

        #endregion

        #region Overriden Http Server methods

        public void AddStreamHandler(string method, IRequestHandler handler)
        {
            Server.AddStreamHandler(handler);
            AddCAPS(method, handler.Path);
        }

        public void RemoveStreamHandler(string method, string httpMethod, string path)
        {
            Server.RemoveStreamHandler(httpMethod, path);
            RemoveCaps(method);
        }

        #endregion

        #region SEED cap handling

        public void AddSEEDCap(string CapsUrl, string UrlToInform)
        {
            if (CapsUrl != "")
                m_capsUrlBase = CapsUrl;
            if (UrlToInform != "" && UrlToInform != this.CapsUrl)
                m_UrlToInform = UrlToInform;
            Disabled = false;
            //Add our SEED cap
            AddStreamHandler("SEED", new RestStreamHandler("POST", m_capsUrlBase, CapsRequest));
        }

        public void Close()
        {
            //Remove our SEED cap
            RemoveStreamHandler("SEED", "POST", m_capsUrlBase);
        }

        public virtual string CapsRequest(string request, string path, string param,
                                  OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            try
            {
                m_log.Debug("[CapsHandlers]: Handling Seed Cap request at " + CapsUrl + ", informing URL " + UrlToInform);
                if (UrlToInform != "")
                {
                    string reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            UrlToInform,
                            OSDParser.SerializeLLSDXmlString(new OSDMap()));
                    if (reply != "")
                    {
                        OSDMap hash = (OSDMap)OSDParser.DeserializeLLSDXml(Utils.StringToBytes(reply));
                        foreach (string key in hash.Keys)
                        {
                            if (key == null || hash[key] == null)
                                continue;
                            if (!registeredCAPS.ContainsKey(key))
                                registeredCAPS[key] = hash[key].AsString();
                        }
                    }
                    else
                        m_log.Error("[PerRegionCapsService]: Failed to get info for SEED caps from " + UrlToInform);
                }
            }
            catch(Exception ex)
            {
                m_log.Error("[PerRegionCapsService]: Exception getting info for SEED caps from " + UrlToInform + ", " + ex.ToString());
            }
            return OSDParser.SerializeLLSDXmlString(registeredCAPS);
        }

        #endregion

        #region Add/Remove known caps

        protected void AddCAPS()
        {
            List<ICapsServiceConnector> connectors = GetServiceConnectors();
            foreach (ICapsServiceConnector connector in connectors)
            {
                connector.RegisterCaps(this);
            }
        }

        protected void RemoveCAPS()
        {
            List<ICapsServiceConnector> connectors = GetServiceConnectors();
            foreach (ICapsServiceConnector connector in connectors)
            {
                connector.DeregisterCaps();
            }
            Close();
        }

        public void InformModulesOfRequest()
        {
            List<ICapsServiceConnector> connectors = GetServiceConnectors();
            foreach (ICapsServiceConnector connector in connectors)
            {
                connector.EnteringRegion();
            }
        }

        public List<ICapsServiceConnector> GetServiceConnectors()
        {
            if (m_connectors.Count == 0)
            {
                m_connectors = AuroraModuleLoader.PickupModules<ICapsServiceConnector>();
            }
            return m_connectors;
        }

        #endregion
    }
}
