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
using System.Net;
using System.Reflection;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using PresenceInfo = OpenSim.Services.Interfaces.PresenceInfo;

namespace OpenSim.Region.CoreModules.Avatar.InstantMessage
{
    public class PresenceModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        protected List<Scene> m_Scenes = new List<Scene>();

        public void Initialise(IConfigSource config)
        {
        }

        public void AddRegion(Scene scene)
        {
            m_Scenes.Add(scene);

            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnClosingClient += OnClosingClient;
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
            m_Scenes.Remove(scene);

            scene.EventManager.OnNewClient -= OnNewClient;
            scene.EventManager.OnClosingClient -= OnClosingClient;
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "PresenceModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void OnNewClient(IClientAPI client)
        {
            client.AddGenericPacketHandler("requestonlinenotification", OnRequestOnlineNotification);
        }

        private void OnClosingClient(IClientAPI client)
        {
            client.RemoveGenericPacketHandler("requestonlinenotification");
        }

        public void OnRequestOnlineNotification(Object sender, string method, List<String> args)
        {
            if (!(sender is IClientAPI))
                return;

            IClientAPI client = (IClientAPI)sender;
            m_log.DebugFormat("[PRESENCE MODULE]: OnlineNotification requested by {0}", client.Name);

            UserInfo[] status = m_Scenes[0].RequestModuleInterface<IAgentInfoService>().GetUserInfos(args.ToArray());

            List<UUID> online = new List<UUID>();
            List<UUID> offline = new List<UUID>();

            foreach (UserInfo pi in status)
            {
                UUID uuid = new UUID(pi.UserID);
                if (pi.IsOnline && !online.Contains(uuid))
                    online.Add(uuid);
                if (!pi.IsOnline && !offline.Contains(uuid))
                    offline.Add(uuid);
            }

            if (online.Count > 0)
                client.SendAgentOnline(online.ToArray());
            if (offline.Count > 0)
                client.SendAgentOffline(offline.ToArray());
        }
    }
}
