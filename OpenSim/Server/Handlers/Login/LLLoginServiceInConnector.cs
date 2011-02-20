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
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using Aurora.Simulation.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;

namespace OpenSim.Server.Handlers.Login
{
    public class LLLoginServiceInConnector : IService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private ILoginService m_LoginService;
        private bool m_Proxy;

        private IConfigSource m_Config;
        public string Name
        {
            get { return GetType().Name; }
        }

        public void Initialize(IConfigSource config, IRegistryCore registry)
        {
        }

        public void Start(IConfigSource config, IRegistryCore registry)
        {
            IConfig handlerConfig = config.Configs["Handlers"];
            if (handlerConfig.GetString("LLLoginHandler", "") != Name)
                return;

            IHttpServer server = registry.RequestModuleInterface<ISimulationBase>().GetHttpServer((uint)handlerConfig.GetInt("LLLoginHandlerPort"));
            m_log.Debug("[LLLOGIN IN CONNECTOR]: Starting...");
            ReadLocalServiceFromConfig(config);
            m_LoginService = registry.RequestModuleInterface<ILoginService>();

            InitializeHandlers(server);
        }

        public void FinishedStartup()
        {
        }

        private void ReadLocalServiceFromConfig(IConfigSource config)
        {
            m_Config = config;
            IConfig serverConfig = config.Configs["LoginService"];
            if (serverConfig == null)
                throw new Exception(String.Format("No section LoginService in config file"));

            m_Proxy = serverConfig.GetBoolean("HasProxy", false);
        }

        private void InitializeHandlers(IHttpServer server)
        {
            LLLoginHandlers loginHandlers = new LLLoginHandlers(m_LoginService, m_Config, m_Proxy);
            server.AddXmlRPCHandler("login_to_simulator", loginHandlers.HandleXMLRPCLogin, false);
            server.AddXmlRPCHandler("set_login_level", loginHandlers.HandleXMLRPCSetLoginLevel, false);
            server.SetDefaultLLSDHandler(loginHandlers.HandleLLSDLogin);
        }
    }
}
