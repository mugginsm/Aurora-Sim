/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyrightD
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
using System.Threading;
using System.Reflection;
using log4net;
using Nini.Config;
using Aurora.DataManager;
using Aurora.Framework;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace OpenSim.Region.Framework.Scenes
{
    public class Scene : RegistryCore, IScene
    {
        #region Fields

        private static readonly ILog m_log
            = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public List<SceneObjectGroup> PhysicsReturns = new List<SceneObjectGroup>();

        /// <value>
        /// The scene graph for this scene
        /// </value>
        private SceneGraph m_sceneGraph;

        protected readonly ClientManager m_clientManager = new ClientManager();

        protected RegionInfo m_regInfo;

        protected ThreadMonitor monitor = new ThreadMonitor();
            
        protected AuroraEventManager m_AuroraEventManager = null;
        protected EventManager m_eventManager;
        /// <value>
        /// Manage events that occur in this scene (avatar movement, script rez, etc.).  Commonly used by region modules
        /// to subscribe to scene events.
        /// </value>
        public EventManager EventManager
        {
            get { return m_eventManager; }
        }
        /// <summary>
        /// Generic manager to send and recieve events. Used mainly by region modules
        /// </summary>
        public AuroraEventManager AuroraEventManager
        {
            get { return m_AuroraEventManager; }
        }

        private SceneManager m_sceneManager;

        public SceneManager SceneManager
        {
            get { return m_sceneManager; }
        }

        protected ScenePermissions m_permissions;
        /// <summary>
        /// Controls permissions for the Scene
        /// </summary>
        public ScenePermissions Permissions
        {
            get { return m_permissions; }
        }

        protected IConfigSource m_config;

        protected AgentCircuitManager m_authenticateHandler;

        // Central Update Loop

        protected uint m_frame;
        /// <summary>
        /// The current frame #
        /// </summary>
        public uint Frame
        {
            get { return m_frame; }
        }

        private float m_basesimfps = 45f;
        private float m_basesimphysfps = 45f;

        protected float m_updatetimespan = 0.022f;
        protected float m_physicstimespan = 0.022f;
        protected DateTime m_lastphysupdate = DateTime.UtcNow;

        private int m_update_physics = 1; //Trigger the physics update
        private int m_update_entities = 5; // Send prim updates for clients
        private int m_update_events = 1; //Trigger the OnFrame event and tell any modules about the new frame
        private int m_update_coarse_locations = 30; //Trigger the sending of coarse location updates (minimap updates)

        private volatile bool shuttingdown = false;

        public bool CheckForObjectCulling = false;
        private string m_DefaultObjectName = "Primitive";
        public bool m_usePreJump = true;
        public bool m_useSplatAnimation = true;
        public float MaxLowValue = -1000;
        private Dictionary<UUID, AgentData> m_incomingChildAgentData = new Dictionary<UUID, AgentData>();

        #endregion

        #region Properties

        public float BaseSimFPS
        {
            get { return m_basesimfps; }
        }

        public float BaseSimPhysFPS
        {
            get { return m_basesimphysfps; }
        }

        public AgentCircuitManager AuthenticateHandler
        {
            get { return m_authenticateHandler; }
        }

        public ISimulationDataStore SimulationDataService
        {
            get { return m_sceneManager.SimulationDataService; }
        }

        public bool ShuttingDown
        {
            get { return shuttingdown; }
        }

        public SceneGraph SceneGraph
        {
            get { return m_sceneGraph; }
        }

        public RegionInfo RegionInfo
        {
            get { return m_regInfo; }
        }

        // This gets locked so things stay thread safe.
        public object SyncRoot
        {
            get { return m_sceneGraph.m_syncRoot; }
        }

        public EntityManager Entities
        {
            get { return m_sceneGraph.Entities; }
        }

        public IConfigSource Config
        {
            get { return m_config; }
            set { m_config = value; }
        }

        public string DefaultObjectName
        {
            get { return m_DefaultObjectName; }
        }

        public float TimeDilation
        {
            get { return m_sceneGraph.PhysicsScene.TimeDilation; }
            set { m_sceneGraph.PhysicsScene.TimeDilation = value; }
        }

        public override string ToString()
        {
            return "Name: " + m_regInfo.RegionName + ", Loc: " +
                m_regInfo.RegionLocX / Constants.RegionSize + "," +
                m_regInfo.RegionLocY / Constants.RegionSize + ", Size: " +
                m_regInfo.RegionSizeX + "," +
                m_regInfo.RegionSizeY + 
                ", Port: " + m_regInfo.InternalEndPoint.Port;
        }

        #region Services

        public IAssetService AssetService
        {
            get
            {
                return RequestModuleInterface<IAssetService>();
            }
        }

        public IAuthenticationService AuthenticationService
        {
            get
            {
                return RequestModuleInterface<IAuthenticationService>();
            }
        }

        public IAvatarService AvatarService
        {
            get
            {
                return RequestModuleInterface<IAvatarService>();
            }
        }

        public IGridService GridService
        {
            get
            {
                return RequestModuleInterface<IGridService>();
            }
        }

        public IInventoryService InventoryService
        {
            get
            {
                return RequestModuleInterface<IInventoryService>();
            }
        }

        public ISimulationService SimulationService
        {
            get
            {
                return RequestModuleInterface<ISimulationService>();
            }
        }

        public IUserAccountService UserAccountService
        {
            get
            {
                return RequestModuleInterface<IUserAccountService>();
            }
        }

        #endregion

        #endregion

        #region Constructors

        public Scene(RegionInfo regInfo)
        {
            m_regInfo = regInfo;
        }

        public Scene(RegionInfo regInfo, AgentCircuitManager authen, SceneManager manager)
            : this(regInfo)
        {
            m_sceneManager = manager;

            m_config = manager.ConfigSource;
            m_authenticateHandler = authen;

            m_AuroraEventManager = new AuroraEventManager();
            m_eventManager = new EventManager();
            m_permissions = new ScenePermissions(this);

            // Load region settings
            m_regInfo.RegionSettings = m_sceneManager.SimulationDataService.LoadRegionSettings(m_regInfo.RegionID);

            m_sceneGraph = new SceneGraph(this, m_regInfo);

            #region Region Config

            IConfig aurorastartupConfig = m_config.Configs["AuroraStartup"];
            if (aurorastartupConfig != null)
            {
                MaxLowValue = aurorastartupConfig.GetFloat("MaxLowValue", -1000);
                m_DefaultObjectName = aurorastartupConfig.GetString("DefaultObjectName", m_DefaultObjectName);
                CheckForObjectCulling = aurorastartupConfig.GetBoolean("CheckForObjectCulling", CheckForObjectCulling);
                //Region specific is still honored here, the RegionInfo checks for it
                RegionInfo.ObjectCapacity = aurorastartupConfig.GetInt("ObjectCapacity", 80000);
            }

            //Animation states
            IConfig animationConfig = m_config.Configs["Animations"];
            if (animationConfig != null)
            {
                m_usePreJump = animationConfig.GetBoolean("enableprejump", m_usePreJump);
                m_useSplatAnimation = animationConfig.GetBoolean("enableSplatAnimation", m_useSplatAnimation);
            }

            IConfig packetConfig = m_config.Configs["PacketPool"];
            if (packetConfig != null)
            {
                PacketPool.Instance.RecyclePackets = packetConfig.GetBoolean("RecyclePackets", true);
                PacketPool.Instance.RecycleDataBlocks = packetConfig.GetBoolean("RecycleDataBlocks", true);
            }

            #endregion Region Config

            m_basesimfps = 45f;
            m_basesimphysfps = 45f;

            m_basesimphysfps = Config.Configs["Physics"].GetFloat("BasePhysicsFPS", 45f);
            if (m_basesimphysfps > 45f)
                m_basesimphysfps = 45f;

            m_basesimfps = Config.Configs["Protection"].GetFloat("BaseRateFramesPerSecond", 45f);
            if (m_basesimfps > 45f)
                m_basesimfps = 45f;

            if (m_basesimphysfps > m_basesimfps)
                m_basesimphysfps = m_basesimfps;

            m_updatetimespan = 1 / m_basesimfps;
            m_physicstimespan = 1 / m_basesimphysfps;

            #region Startup Complete config

            EventManager.OnAddToStartupQueue += AddToStartupQueue;
            EventManager.OnModuleFinishedStartup += FinishedStartup;
            EventManager.OnStartupComplete += StartupComplete;

            AddToStartupQueue("Startup");

            #endregion
        }

        #endregion Constructors

        #region Close

        /// <summary>
        /// This is the method that shuts down the scene.
        /// </summary>
        public void Close()
        {
            m_log.InfoFormat("[Scene]: Closing down the single simulator: {0}", RegionInfo.RegionName);

            // Kick all ROOT agents with the message, 'The simulator is going down'
            ForEachScenePresence(delegate(ScenePresence avatar)
            {
                if (!avatar.IsChildAgent)
                    avatar.ControllingClient.Kick("The simulator is going down.");
            });

            ScenePresence[] Presences = new ScenePresence[ScenePresences.Count];
            ScenePresences.CopyTo(Presences, 0);
            foreach (ScenePresence avatar in Presences)
            {
                IncomingCloseAgent(avatar.UUID);
            }
            //Stop the heartbeat
            monitor.Stop();

            if (SceneGraph.PhysicsScene != null)
                SceneGraph.PhysicsScene.Dispose();

            //Tell the neighbors that this region is now down
            INeighborService service = RequestModuleInterface<INeighborService>();
            if (service != null)
                service.InformNeighborsThatRegionIsDown(RegionInfo);

            // Stop updating the scene objects and agents.
            shuttingdown = true;

            m_sceneGraph.Close();
        }

        #endregion

        #region Tracker

        /// <summary>
        /// Start the timer which triggers regular scene updates
        /// </summary>
        public void StartHeartbeat()
        {
            //Give it the heartbeat delegate with an infinite timeout
            monitor.StartTrackingThread(0, Update);
            //Then start the thread for it with an infinite loop time and no 
            //  sleep overall as the Update delete does it on it's own
            monitor.StartMonitor(0, 0);
        }

        #endregion

        #region Scene Heartbeat Methods

        private void Update()
        {
            ISimFrameMonitor simFrameMonitor = (ISimFrameMonitor)RequestModuleInterface<IMonitorModule>().GetMonitor(RegionInfo.RegionID.ToString(), "SimFrameStats");
            ITotalFrameTimeMonitor totalFrameMonitor = (ITotalFrameTimeMonitor)RequestModuleInterface<IMonitorModule>().GetMonitor(RegionInfo.RegionID.ToString(), "Total Frame Time");
            ISetMonitor lastFrameMonitor = (ISetMonitor)RequestModuleInterface<IMonitorModule>().GetMonitor(RegionInfo.RegionID.ToString(), "Last Completed Frame At");
            ITimeMonitor otherFrameMonitor = (ITimeMonitor)RequestModuleInterface<IMonitorModule>().GetMonitor(RegionInfo.RegionID.ToString(), "Other Frame Time");
            ITimeMonitor sleepFrameMonitor = (ITimeMonitor)RequestModuleInterface<IMonitorModule>().GetMonitor(RegionInfo.RegionID.ToString(), "Sleep Frame Time");
            IPhysicsFrameMonitor physicsFrameMonitor = (IPhysicsFrameMonitor)RequestModuleInterface<IMonitorModule>().GetMonitor(RegionInfo.RegionID.ToString(), "Total Physics Frame Time");
            ITimeMonitor physicsSyncFrameMonitor = (ITimeMonitor)RequestModuleInterface<IMonitorModule>().GetMonitor(RegionInfo.RegionID.ToString(), "Physics Sync Frame Time");
            ITimeMonitor physicsFrameTimeMonitor = (ITimeMonitor)RequestModuleInterface<IMonitorModule>().GetMonitor(RegionInfo.RegionID.ToString(), "Physics Update Frame Time");
            int maintc = Util.EnvironmentTickCount();
            int BeginningFrameTime = maintc;

            // Increment the frame counter
            ++m_frame;

            try
            {
                int OtherFrameTime = Util.EnvironmentTickCount();
                if (PhysicsReturns.Count != 0)
                {
                    lock (PhysicsReturns)
                    {
                        ILLClientInventory inventoryModule = RequestModuleInterface<ILLClientInventory>();
                        if (inventoryModule != null)
                            inventoryModule.ReturnObjects(PhysicsReturns.ToArray(), UUID.Zero);
                        PhysicsReturns.Clear();
                    }
                }
                if (m_frame % m_update_coarse_locations == 0)
                {
                    List<Vector3> coarseLocations;
                    List<UUID> avatarUUIDs;
                    SceneGraph.GetCoarseLocations(out coarseLocations, out avatarUUIDs, 60);
                    // Send coarse locations to clients 
                    foreach (ScenePresence presence in ScenePresences)
                    {
                        presence.SendCoarseLocations(coarseLocations, avatarUUIDs);
                    }
                }

                if (m_frame % m_update_entities == 0)
                    m_sceneGraph.UpdateEntities();

                if (m_frame % m_update_events == 0)
                    m_eventManager.TriggerOnFrame();

                int PhysicsSyncTime = Util.EnvironmentTickCount();
                TimeSpan SinceLastFrame = DateTime.UtcNow - m_lastphysupdate;

                if ((m_frame % m_update_physics == 0) && !RegionInfo.RegionSettings.DisablePhysics)
                    m_sceneGraph.UpdatePreparePhysics();

                int MonitorPhysicsSyncTime = Util.EnvironmentTickCountSubtract(PhysicsSyncTime);

                int PhysicsUpdateTime = Util.EnvironmentTickCount();

                if (m_frame % m_update_physics == 0)
                {
                    if (!RegionInfo.RegionSettings.DisablePhysics && SinceLastFrame.TotalSeconds > m_physicstimespan)
                    {
                        m_sceneGraph.UpdatePhysics(SinceLastFrame.TotalSeconds);
                        m_lastphysupdate = DateTime.UtcNow;
                    }
                }

                int MonitorPhysicsUpdateTime = Util.EnvironmentTickCountSubtract(PhysicsUpdateTime) + MonitorPhysicsSyncTime;

                physicsFrameTimeMonitor.AddTime(MonitorPhysicsUpdateTime);
                physicsFrameMonitor.AddFPS(1);
                physicsSyncFrameMonitor.AddTime(MonitorPhysicsSyncTime);

                IPhysicsMonitor monitor = RequestModuleInterface<IPhysicsMonitor>();
                if (monitor != null)
                    monitor.AddPhysicsStats(RegionInfo.RegionID, m_sceneGraph.PhysicsScene);

                //Now fix the sim stats
                int MonitorOtherFrameTime = Util.EnvironmentTickCountSubtract(OtherFrameTime);
                int MonitorLastCompletedFrame = Util.EnvironmentTickCount();

                simFrameMonitor.AddFPS(1);
                lastFrameMonitor.SetValue(MonitorLastCompletedFrame);
                otherFrameMonitor.AddTime(MonitorOtherFrameTime);

                maintc = Util.EnvironmentTickCountSubtract(maintc);
                maintc = (int)(m_updatetimespan * 1000) - maintc;
            }
            catch (Exception e)
            {
                m_log.Error("[REGION]: Failed with exception " + e.ToString() + " in region: " + RegionInfo.RegionName);
                return;
            }

            int MonitorEndFrameTime = Util.EnvironmentTickCountSubtract(BeginningFrameTime) + maintc;

            if (maintc > 0)
                Thread.Sleep(maintc);

            sleepFrameMonitor.AddTime(maintc);

            totalFrameMonitor.AddFrameTime(MonitorEndFrameTime);
        }

        #endregion

        #region Add/Remove Avatar Methods

        /// <summary>
        /// Adding a New Client and Create a Presence for it.
        /// Called by the LLClientView when the UseCircuitCode packet comes in
        /// Used by NPCs to add themselves to the Scene
        /// </summary>
        /// <param name="client"></param>
        public void AddNewClient(IClientAPI client)
        {
            try
            {
                System.Net.IPEndPoint ep = (System.Net.IPEndPoint)client.GetClientEP();
                AgentCircuitData aCircuit = AuthenticateHandler.AuthenticateSession(client.SessionId, client.AgentId, client.CircuitCode, ep);

                if (aCircuit == null) // no good, didn't pass NewUserConnection successfully
                    return;

                //Create the scenepresence, then update it with any info that we have about it
                ScenePresence sp = m_sceneGraph.CreateAndAddChildScenePresence(client);
                lock (m_incomingChildAgentData)
                {
                    if (m_incomingChildAgentData.ContainsKey(sp.UUID))
                    {
                        //Found info, update the agent then remove it
                        sp.ChildAgentDataUpdate(m_incomingChildAgentData[sp.UUID]);
                        m_incomingChildAgentData.Remove(sp.UUID);
                    }
                }
                //Make sure the appearanace is updated
                if (aCircuit != null)
                    sp.Appearance = aCircuit.Appearance;
                sp.IsChildAgent = aCircuit.child;

                m_clientManager.Add(client);

                //Trigger events
                m_eventManager.TriggerOnNewPresence(sp);

                if (GetScenePresence(client.AgentId) != null)
                {
                    EventManager.TriggerOnNewClient(client);
                    if ((aCircuit.teleportFlags & (uint)TeleportFlags.ViaLogin) != 0)
                        EventManager.TriggerOnClientLogin(client);
                }

                //Add the client to login stats
                ILoginMonitor monitor = (ILoginMonitor)RequestModuleInterface<IMonitorModule>().GetMonitor("", "LoginMonitor");
                if ((aCircuit.teleportFlags & (uint)TeleportFlags.ViaLogin) != 0 && monitor != null)
                {
                    monitor.AddSuccessfulLogin();
                }
            }
            catch(Exception ex)
            {
                m_log.Warn("[Scene]: Error in AddNewClient: " + ex.ToString());
            }
        }

        #endregion

        #region RegionComms

        /// <summary>
        /// Do the work necessary to initiate a new user connection for a particular scene.
        /// At the moment, this consists of setting up the caps infrastructure
        /// The return bool should allow for connections to be refused, but as not all calling paths
        /// take proper notice of it let, we allowed banned users in still.
        /// </summary>
        /// <param name="agent">CircuitData of the agent who is connecting</param>
        /// <param name="reason">Outputs the reason for the false response on this string,
        /// If the agent was accepted, this will be the Caps SEED for the region</param>
        /// <param name="requirePresenceLookup">True for normal presence. False for NPC
        /// or other applications where a full grid/Hypergrid presence may not be required.</param>
        /// <returns>True if the region accepts this agent.  False if it does not.  False will 
        /// also return a reason.</returns>
        public bool NewUserConnection(AgentCircuitData agent, uint teleportFlags, out string reason)
        {
            bool vialogin = ((teleportFlags & (uint)TeleportFlags.ViaLogin) != 0);
            reason = String.Empty;

            // Don't disable this log message - it's too helpful
            m_log.DebugFormat(
                "[ConnectionBegin]: Region {0} told of incoming {1} agent {2} (circuit code {3}, teleportflags {4})",
                RegionInfo.RegionName, (agent.child ? "child" : "root"), agent.AgentID,
                agent.circuitcode, teleportFlags);

            if (!AuthorizeUser(agent, out reason))
                return false;

            ScenePresence sp = GetScenePresence(agent.AgentID);

            ICapsService capsService = RequestModuleInterface<ICapsService>();
            if (sp != null && !sp.IsChildAgent)
            {
                // We have a zombie from a crashed session. 
                // Or the same user is trying to be root twice here, won't work.
                // Kill it.
                m_log.InfoFormat("[Scene]: Zombie scene presence detected for {0} in {1}", agent.AgentID, RegionInfo.RegionName);
                RemoveAgent(sp);
                //Destroy the old caps
                IClientCapsService clientCaps = capsService.GetClientCapsService(sp.UUID);
                if (clientCaps != null)
                    clientCaps.RemoveCAPS(this.RegionInfo.RegionHandle);
                sp = null;
            }

            OSDMap responseMap = new OSDMap();

            if (capsService != null)
            {
                const string seedRequestPath = "0000/";
                string CapsSeed = "/CAPS/" + agent.CapsPath + seedRequestPath;
                string capsUrl = capsService.CreateCAPS(agent.AgentID, "", CapsSeed, RegionInfo.RegionHandle, !agent.child, agent);
                IRegionClientCapsService regionCaps = capsService.GetClientCapsService(agent.AgentID).GetCapsService(RegionInfo.RegionHandle);

                regionCaps.AddSEEDCap("", "");

                m_log.Debug("[NewAgentConnection]: Adding Caps Url for region " + RegionInfo.RegionName +
                     " @" + capsUrl + " for agent " + agent.AgentID);
                responseMap["CapsUrl"] = capsUrl;
            }

            // In all cases, add or update the circuit data with the new agent circuit data and teleport flags
            agent.teleportFlags = teleportFlags;

            //Add the circuit at the end
            AuthenticateHandler.AddNewCircuit(agent.circuitcode, agent);

            m_log.InfoFormat(
                "[ConnectionBegin]: Region {0} authenticated and authorized incoming {1} agent {2} (circuit code {3})",
                RegionInfo.RegionName, (agent.child ? "child" : "root"), agent.AgentID,
                agent.circuitcode);

            reason = OSDParser.SerializeJsonString(responseMap);
            return true;
        }

        /// <summary>
        /// Verify if the user can connect to this region.  Checks the banlist and ensures that the region is set for public access
        /// </summary>
        /// <param name="agent">The circuit data for the agent</param>
        /// <param name="reason">outputs the reason to this string</param>
        /// <returns>True if the region accepts this agent.  False if it does not.  False will 
        /// also return a reason.</returns>
        protected bool AuthorizeUser(AgentCircuitData agent, out string reason)
        {
            reason = String.Empty;

            IAuthorizationService AuthorizationService = RequestModuleInterface<IAuthorizationService>();
            if (AuthorizationService != null)
            {
                GridRegion ourRegion = new GridRegion(RegionInfo);
                if (!AuthorizationService.IsAuthorizedForRegion(ourRegion, agent, !agent.child, out reason))
                {
                    m_log.WarnFormat("[ConnectionBegin]: Denied access to {0} at {1} because the user does not have access to the region, reason: {2}",
                                     agent.AgentID, RegionInfo.RegionName, reason);
                    reason = String.Format("You do not have access to the region {0}, reason: {1}", RegionInfo.RegionName, reason);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// We've got an update about an agent that sees into this region, 
        /// send it to ScenePresence for processing  It's the full data.
        /// </summary>
        /// <param name="cAgentData">Agent that contains all of the relevant things about an agent.
        /// Appearance, animations, position, etc.</param>
        /// <returns>true if we handled it.</returns>
        public virtual bool IncomingChildAgentDataUpdate(AgentData cAgentData)
        {
            //m_log.DebugFormat(
            //    "[SCENE]: Incoming child agent update for {0} in {1}", cAgentData.AgentID, RegionInfo.RegionName);

            // XPTO: if this agent is not allowed here as root, always return false

            // We have to wait until the viewer contacts this region after receiving EAC.
            // That calls AddNewClient, which finally creates the ScenePresence and then this gets set up

            //No null updates!
            if (cAgentData == null)
                return false;

            ScenePresence SP = GetScenePresence(cAgentData.AgentID);
            if (SP != null)
                SP.ChildAgentDataUpdate(cAgentData);
            else
                lock(m_incomingChildAgentData)
                    m_incomingChildAgentData[cAgentData.AgentID] = cAgentData;
            return true;
        }

        /// <summary>
        /// We've got an update about an agent that sees into this region, 
        /// send it to ScenePresence for processing  It's only positional data
        /// </summary>
        /// <param name="cAgentData">AgentPosition that contains agent positional data so we can know what to send</param>
        /// <returns>true if we handled it.</returns>
        public virtual bool IncomingChildAgentDataUpdate(AgentPosition cAgentData)
        {
            //m_log.Debug(" XXX Scene IncomingChildAgentDataUpdate POSITION in " + RegionInfo.RegionName);
            ScenePresence presence = GetScenePresence(cAgentData.AgentID);
            if (presence != null)
            {
                // I can't imagine *yet* why we would get an update if the agent is a root agent..
                // however to avoid a race condition crossing borders..
                if (presence.IsChildAgent)
                {
                    uint rRegionX = 0;
                    uint rRegionY = 0;
                    //In meters
                    Utils.LongToUInts(cAgentData.RegionHandle, out rRegionX, out rRegionY);
                    //In meters
                    int tRegionX = RegionInfo.RegionLocX;
                    int tRegionY = RegionInfo.RegionLocY;
                    //Send Data to ScenePresence
                    presence.ChildAgentDataUpdate(cAgentData, tRegionX, tRegionY, (int)rRegionX, (int)rRegionY);
                }

                return true;
            }

            return false;
        }

        public virtual bool IncomingRetrieveRootAgent(UUID id, out IAgentData agent)
        {
            agent = null;
            ScenePresence sp = GetScenePresence(id);
            if ((sp != null) && (!sp.IsChildAgent))
            {
                sp.IsChildAgent = true;
                AgentData data = new AgentData();
                sp.CopyTo(data);
                agent = data;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tell a single agent to disconnect from the region.
        /// </summary>
        /// <param name="regionHandle"></param>
        /// <param name="agentID"></param>
        public bool IncomingCloseAgent(UUID agentID)
        {
            //m_log.DebugFormat("[SCENE]: Processing incoming close agent for {0}", agentID);

            ScenePresence presence = m_sceneGraph.GetScenePresence(agentID);
            if (presence != null)
            {
                if (!presence.IsChildAgent)
                {
                    INeighborService service = RequestModuleInterface<INeighborService>();
                    if (service != null)
                        service.CloseAllNeighborAgents(presence.UUID, RegionInfo.RegionID);
                }
                bool RetVal = RemoveAgent(presence);

                ISyncMessagePosterService syncPoster = RequestModuleInterface<ISyncMessagePosterService>();
                if (syncPoster != null)
                {
                    //Make sure that the disable simulator packet doesn't kill root agents right now... it kills the client

                    // Don't do this to root agents on logout, it's not nice for the viewer
                    // Tell a single agent to disconnect from the region.

                    syncPoster.Post(SyncMessageHelper.DisableSimulator(presence.UUID, RegionInfo.RegionHandle));

                    //Kill the client's connection to this sim...
                    presence.ControllingClient.Stop();
                }

                return RetVal;
            }

            // Agent not here
            return false;
        }

        /// <summary>
        /// Tell a single agent to disconnect from the region.
        /// Does not send the DisableSimulator EQM or close child agents
        /// </summary>
        /// <param name="?"></param>
        /// <returns></returns>
        public bool RemoveAgent(ScenePresence presence)
        {
            presence.ControllingClient.Close();
            if (presence.ParentID != UUID.Zero)
            {
                presence.StandUp();
            }

            m_eventManager.TriggerClientClosed(presence.UUID, this);
            m_eventManager.TriggerOnClosingClient(presence.ControllingClient);
            m_eventManager.TriggerOnRemovePresence(presence);

            ForEachClient(
                delegate(IClientAPI client)
                {
                    //We can safely ignore null reference exceptions.  It means the avatar is dead and cleaned up anyway
                    try { client.SendKillObject(presence.Scene.RegionInfo.RegionHandle, new ISceneEntity[] { presence }); }
                    catch (NullReferenceException) { }
                });

            try
            {
                presence.Close();
            }
            catch (Exception e)
            {
                m_log.Error("[SCENE] Scene.cs:RemoveClient exception: " + e.ToString());
            }

            // Remove the avatar from the scene
            m_sceneGraph.RemoveScenePresence(presence.UUID);
            m_clientManager.Remove(presence.UUID);

            AuthenticateHandler.RemoveCircuit(presence.ControllingClient.CircuitCode);
            //m_log.InfoFormat("[SCENE] Memory pre  GC {0}", System.GC.GetTotalMemory(false));
            //m_log.InfoFormat("[SCENE] Memory post GC {0}", System.GC.GetTotalMemory(true));
            return true;
        }

        #endregion

        #region SceneGraph wrapper methods

        /// <summary>
        /// Request a scene presence by UUID. Fast, indexed lookup.
        /// </summary>
        /// <param name="agentID"></param>
        /// <returns>null if the presence was not found</returns>
        public ScenePresence GetScenePresence(UUID agentID)
        {
            return m_sceneGraph.GetScenePresence(agentID);
        }

        /// <summary>
        /// Performs action on all scene presences.
        /// </summary>
        /// <param name="action"></param>
        public void ForEachScenePresence(Action<ScenePresence> action)
        {
            if (m_sceneGraph != null)
            {
                m_sceneGraph.ForEachScenePresence(action);
            }
        }

        public List<ScenePresence> ScenePresences
        {
            get { return m_sceneGraph.ScenePresences; }
        }

        /// <summary>
        /// Get a prim via its local id
        /// </summary>
        /// <param name="localID"></param>
        /// <returns></returns>
        public SceneObjectPart GetSceneObjectPart(uint localID)
        {
            ISceneEntity entity;
            m_sceneGraph.TryGetPart(localID, out entity);
            return entity as SceneObjectPart;
        }

        /// <summary>
        /// Get a prim via its UUID
        /// </summary>
        /// <param name="fullID"></param>
        /// <returns></returns>
        public SceneObjectPart GetSceneObjectPart(UUID ObjectID)
        {
            ISceneEntity entity;
            m_sceneGraph.TryGetPart(ObjectID, out entity);
            return entity as SceneObjectPart;
        }

        /// <summary>
        /// Get a scene object group that contains the prim with the given local id
        /// </summary>
        /// <param name="localID"></param>
        /// <returns>null if no scene object group containing that prim is found</returns>
        public SceneObjectGroup GetGroupByPrim(uint localID)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);
            if (part != null)
                return part.ParentGroup;
            return null;
        }

        public bool TryGetScenePresence(UUID agentID, out IScenePresence scenePresence)
        {
            scenePresence = null;
            ScenePresence sp = null;
            if (TryGetScenePresence(agentID, out sp))
            {
                scenePresence = sp;
                return true;
            }

            return false;
        }

        public bool TryGetScenePresence(UUID avatarId, out ScenePresence avatar)
        {
            return m_sceneGraph.TryGetScenePresence(avatarId, out avatar);
        }

        public bool TryGetAvatarByName(string avatarName, out ScenePresence avatar)
        {
            return m_sceneGraph.TryGetAvatarByName(avatarName, out avatar);
        }

        public void ForEachClient(Action<IClientAPI> action)
        {
            m_clientManager.ForEachSync(action);
        }

        public bool TryGetClient(UUID avatarID, out IClientAPI client)
        {
            return m_clientManager.TryGetValue(avatarID, out client);
        }

        public bool TryGetClient(System.Net.IPEndPoint remoteEndPoint, out IClientAPI client)
        {
            return m_clientManager.TryGetValue(remoteEndPoint, out client);
        }

        public void ForEachSOG(Action<SceneObjectGroup> action)
        {
            m_sceneGraph.ForEachSOG(action);
        }

        #endregion

        #region Startup Complete

        private List<string> StartupCallbacks = new List<string>();
        private List<string> StartupData = new List<string>();

        /// <summary>
        /// Add a module to the startup queue
        /// </summary>
        /// <param name="name"></param>
        public void AddToStartupQueue(string name)
        {
            IConfig startupConfig = m_config.Configs["Startup"];
            if ((startupConfig != null &&
                !startupConfig.GetBoolean("CompleteStartupAfterAllModulesLoad", true)) ||
                name == "Startup") //We allow startup through to allow for normal starting up, even if all module loading is disabled
            {
                StartupCallbacks.Add(name);
            }
        }

        /// <summary>
        /// This module finished startup and is giving a list of data about its startup
        /// </summary>
        /// <param name="name"></param>
        /// <param name="data"></param>
        public void FinishedStartup(string name, List<string> data)
        {
            if (StartupCallbacks.Contains(name))
            {
                StartupCallbacks.Remove(name);
                if (data.Count != 0)
                {
                    List<string> NewData = new List<string>(data.Count + 2); //Fixed size to reduce memory
                    NewData.Add(name);
                    NewData.Add(data.Count.ToString());
                    NewData.AddRange(data);
                    StartupData.AddRange(NewData);
                }
                if (StartupCallbacks.Count == 0)
                {
                    //All callbacks are done, trigger startup complete
                    EventManager.TriggerStartupComplete(this, StartupData);
                }
            }
        }

        /// <summary>
        /// Startup is complete, trigger the modules and allow logins
        /// </summary>
        /// <param name="data"></param>
        public void StartupComplete(IScene scene, List<string> data)
        {
            // In 99.9% of cases it is a bad idea to manually force garbage collection. However,
            // this is a rare case where we know we have just went through a long cycle of heap
            // allocations, and there is no more work to be done until someone logs in
            GC.Collect();

            m_log.Info("[Region]: Startup Complete in region " + RegionInfo.RegionName);
            
            //Tell the SceneManager about it
            m_sceneManager.HandleStartupComplete(this, data);
        }

        #endregion
    }
}