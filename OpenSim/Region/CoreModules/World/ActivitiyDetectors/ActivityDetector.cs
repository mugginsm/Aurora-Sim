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
using System.Reflection;

using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

using OpenMetaverse;
using log4net;
using Nini.Config;

namespace OpenSim.Region.CoreModules
{
    public class ActivityDetector : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        
        public void Initialise(IConfigSource source)
        {
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnClosingClient += OnClosingClient;
        }

        public void RemoveRegion(Scene scene)
        {
            ISyncMessagePosterService syncMessage = scene.RequestModuleInterface<ISyncMessagePosterService>();
            if (syncMessage != null)
                syncMessage.Post(SyncMessageHelper.LogoutRegionAgents(scene.RegionInfo.RegionHandle));
            scene.EventManager.OnNewClient -= OnNewClient;
            scene.EventManager.OnClosingClient -= OnClosingClient;
        }

        public void RegionLoaded(Scene scene)
        {
            ISyncMessagePosterService syncMessage = scene.RequestModuleInterface<ISyncMessagePosterService>();
            if (syncMessage != null)
                syncMessage.Post(SyncMessageHelper.LogoutRegionAgents(scene.RegionInfo.RegionHandle));
        }

        public string Name
        {
            get { return "ActivityDetector"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void OnNewClient(IClientAPI client)
        {
            client.OnConnectionClosed += OnConnectionClose;
        }

        private void OnClosingClient(IClientAPI client)
        {
            client.OnConnectionClosed -= OnConnectionClose;
        }

        public void OnConnectionClose(IClientAPI client)
        {
            IScenePresence sp = null;
            client.Scene.TryGetScenePresence(client.AgentId, out sp);
            if (client.IsLoggingOut && sp != null & !sp.IsChildAgent)
            {
                m_log.InfoFormat("[ActivityDetector]: Detected client logout {0} in {1}", client.AgentId, client.Scene.RegionInfo.RegionName);
            
                //Inform the grid service about it

                client.Scene.RequestModuleInterface<ISyncMessagePosterService>().Get(SyncMessageHelper.AgentLoggedOut(client.AgentId, client.Scene.RegionInfo.RegionHandle));
            }
        }
    }
}
