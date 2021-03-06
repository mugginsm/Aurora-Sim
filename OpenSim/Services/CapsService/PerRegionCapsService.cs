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
    /// This keeps track of what clients are in the given region
    /// </summary>
    public class PerRegionCapsService : IRegionCapsService
    {
        #region Declares

        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        
        private ulong m_RegionHandle;
        public ulong RegionHandle
        {
            get { return m_RegionHandle; }
        }

        protected Dictionary<UUID, IRegionClientCapsService> m_clientsInThisRegion =
            new Dictionary<UUID, IRegionClientCapsService>();

        #endregion

        #region Initialize

        /// <summary>
        /// Initialise the service
        /// </summary>
        /// <param name="regionHandle"></param>
        /// <param name="regionID"></param>
        public void Initialise(ulong regionHandle)
        {
            m_RegionHandle = regionHandle;
        }

        #endregion

        #region Add/Get/Remove clients

        /// <summary>
        /// Add this client to the region
        /// </summary>
        /// <param name="service"></param>
        public void AddClientToRegion(IRegionClientCapsService service)
        {
            if (!m_clientsInThisRegion.ContainsKey(service.AgentID))
                m_clientsInThisRegion.Add(service.AgentID, service);
            else //Update the client then... this shouldn't ever happen!
                m_clientsInThisRegion[service.AgentID] = service;
        }

        /// <summary>
        /// Remove the client from this region
        /// </summary>
        /// <param name="service"></param>
        public void RemoveClientFromRegion(IRegionClientCapsService service)
        {
            if (m_clientsInThisRegion.ContainsKey(service.AgentID))
                m_clientsInThisRegion.Remove(service.AgentID);
        }

        /// <summary>
        /// Get an agent's Caps by UUID
        /// </summary>
        /// <param name="AgentID"></param>
        /// <returns></returns>
        public IRegionClientCapsService GetClient(UUID AgentID)
        {
            if (m_clientsInThisRegion.ContainsKey(AgentID))
                return m_clientsInThisRegion[AgentID];
            return null;
        }

        /// <summary>
        /// Get all clients in this region
        /// </summary>
        /// <returns></returns>
        public List<IRegionClientCapsService> GetClients()
        {
            return new List<IRegionClientCapsService>(m_clientsInThisRegion.Values);
        }

        #endregion
    }
}
