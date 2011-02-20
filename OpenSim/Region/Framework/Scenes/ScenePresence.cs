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
using System.Timers;
using OpenMetaverse;
using OpenMetaverse.Packets;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes.Animation;
using OpenSim.Region.Physics.Manager;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using OpenSim.Services.Interfaces;
using Aurora.Framework;

namespace OpenSim.Region.Framework.Scenes
{
    public enum ScriptControlled : uint
    {
        CONTROL_ZERO = 0,
        CONTROL_FWD = 1,
        CONTROL_BACK = 2,
        CONTROL_LEFT = 4,
        CONTROL_RIGHT = 8,
        CONTROL_UP = 16,
        CONTROL_DOWN = 32,
        CONTROL_ROT_LEFT = 256,
        CONTROL_ROT_RIGHT = 512,
        CONTROL_LBUTTON = 268435456,
        CONTROL_ML_LBUTTON = 1073741824
    }

    public struct ScriptControllers
    {
        public UUID itemID;
        public SceneObjectPart part;
        public ScriptControlled ignoreControls;
        public ScriptControlled eventControls;
    }

    public delegate void SendCourseLocationsMethod(UUID scene, ScenePresence presence, List<Vector3> coarseLocations, List<UUID> avatarUUIDs);

    public class ScenePresence : EntityBase, IScenePresence
    {
        #region Declares

        //        ~ScenePresence()
//        {
//            m_log.Debug("[ScenePresence] Destructor called");
//        }

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly Array DIR_CONTROL_FLAGS = Enum.GetValues(typeof(Dir_ControlFlags));
        private static readonly Vector3 HEAD_ADJUSTMENT = new Vector3(0f, 0f, 0.3f);
        
        /// <summary>
        /// Experimentally determined "fudge factor" to make sit-target positions
        /// the same as in SecondLife. Fudge factor was tested for 36 different
        /// test cases including prims of type box, sphere, cylinder, and torus,
        /// with varying parameters for sit target location, prim size, prim
        /// rotation, prim cut, prim twist, prim taper, and prim shear. See mantis
        /// issue #1716
        /// </summary>
        private static readonly Vector3 SIT_TARGET_ADJUSTMENT = new Vector3(0.1f, 0.0f, 0.3f);

        public UUID currentParcelUUID = UUID.Zero;

        private ISceneViewer m_sceneViewer;

        /// <value>
        /// The animator for this avatar
        /// </value>
        public ScenePresenceAnimator Animator
        {
            get { return m_animator; }
        }
        protected ScenePresenceAnimator m_animator;

        private Dictionary<UUID, ScriptControllers> scriptedcontrols = new Dictionary<UUID, ScriptControllers>();
        private ScriptControlled IgnoredControls = ScriptControlled.CONTROL_ZERO;
        private ScriptControlled LastCommands = ScriptControlled.CONTROL_ZERO;
        private bool MouseDown = false;
        private SceneObjectGroup proxyObjectGroup;
        public Vector3 lastKnownAllowedPosition;
        public Vector4 CollisionPlane = Vector4.UnitW;

        private bool m_updateflag;
        private uint m_movementflag;
        public bool m_velocityIsDecaying = false;
        public bool m_overrideUserInput = false;
        public double m_endForceTime = 0;
        private UUID m_requestedSitTargetUUID;
        public UUID SittingOnUUID
        {
            get { return m_requestedSitTargetUUID; }
        }

        private bool m_enqueueSendChildAgentUpdate = false;
        private DateTime m_enqueueSendChildAgentUpdateTime = new DateTime();

        public bool SitGround = false;
        public bool m_InitialHasWearablesBeenSent = false;

        private SendCourseLocationsMethod m_sendCourseLocationsMethod;

        private Vector3 m_requestedSitOffset = new Vector3();

        private float m_sitAvatarHeight = 2.0f;

        private int m_godLevel;
        private int m_userLevel;

        public bool m_invulnerable = true;

        private Vector3 m_lastChildAgentUpdatePosition;
        private Vector3 m_lastChildAgentUpdateCamPosition;

        private int m_perfMonMS;

        private bool m_setAlwaysRun;
        
        private bool m_forceFly;
        private bool m_flyDisabled;
        private volatile bool m_creatingPhysicalRepresentation = false;

        private float m_speedModifier = 1.0f;

        private const float SIGNIFICANT_MOVEMENT = 2.0f;

        private Quaternion m_bodyRot= Quaternion.Identity;

        private const int LAND_VELOCITYMAG_MAX = 20;

        // Default AV Height
        private float m_avHeight = 1.56f;

        protected RegionInfo m_regionInfo;
        protected ulong crossingFromRegion;

        private readonly Vector3[] Dir_Vectors = new Vector3[11];

        /// <summary>
        /// Position of agent's camera in world (region cordinates)
        /// </summary>
        protected Vector3 m_CameraCenter;
        /// <summary>
        /// Used for trigging signficant camera movement
        /// </summary>
        protected Vector3 m_lastCameraCenter;
        /// <summary>
        /// This is used for reprioritization of updates so that we can reprioritze every time the position gets too different
        /// </summary>
        protected Vector3 m_lastAbsolutePosition;

        // Use these three vectors to figure out what the agent is looking at
        // Convert it to a Matrix and/or Quaternion
        protected Vector3 m_CameraAtAxis;
        protected Vector3 m_CameraLeftAxis;
        protected Vector3 m_CameraUpAxis;
        private AgentManager.ControlFlags m_AgentControlFlags;
        private Quaternion m_headrotation = Quaternion.Identity;
        private byte m_state;

        private bool m_autopilotMoving;
        private Vector3 m_autoPilotTarget;
        private bool m_sitAtAutoTarget;

        private string m_nextSitAnimation = String.Empty;

        //PauPaw:Proper PID Controler for autopilot************
        private bool m_moveToPositionInProgress;
        private Vector3 m_moveToPositionTarget;

        private bool m_followCamAuto;

        private int m_movementUpdateCount;

        private const int NumMovementsBetweenRayCast = 5;

        private bool CameraConstraintActive;
        //private int m_moveToPositionStateStatus;
        //*****************************************************

        // Agent's Draw distance.
        protected float m_DrawDistance;

        protected AvatarAppearance m_appearance;

        /// <summary>
        /// Implemented Control Flags
        /// </summary>
        private enum Dir_ControlFlags
        {
            DIR_CONTROL_FLAG_FORWARD = AgentManager.ControlFlags.AGENT_CONTROL_AT_POS,
            DIR_CONTROL_FLAG_BACK = AgentManager.ControlFlags.AGENT_CONTROL_AT_NEG,
            DIR_CONTROL_FLAG_LEFT = AgentManager.ControlFlags.AGENT_CONTROL_LEFT_POS,
            DIR_CONTROL_FLAG_RIGHT = AgentManager.ControlFlags.AGENT_CONTROL_LEFT_NEG,
            DIR_CONTROL_FLAG_UP = AgentManager.ControlFlags.AGENT_CONTROL_UP_POS,
            DIR_CONTROL_FLAG_DOWN = AgentManager.ControlFlags.AGENT_CONTROL_UP_NEG,
            DIR_CONTROL_FLAG_FORWARD_NUDGE = AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_AT_POS,
            DIR_CONTROL_FLAG_BACKWARD_NUDGE = AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_AT_NEG,
            DIR_CONTROL_FLAG_DOWN_NUDGE = AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_NEG,
            DIR_CONTROL_FLAG_LEFT_NUDGE = AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_POS,
            DIR_CONTROL_FLAG_RIGHT_NUDGE = AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_NEG
        }
        
        /// <summary>
        /// Position at which a significant movement was made
        /// </summary>
        private Vector3 posLastSignificantMove;

        private UUID CollisionSoundID = UUID.Zero;

        #endregion

        #region Properties

        /// <summary>
        /// Physical scene representation of this Avatar.
        /// </summary>
        public PhysicsActor PhysicsActor
        {
            get { return m_physicsActor; }
            set { m_physicsActor = value; }
        }

        public uint MovementFlag
        {
            get { return m_movementflag; }
            set { m_movementflag = value; }
        }

        public bool Updated
        {
            get { return m_updateflag; }
            set { m_updateflag = value; }
        }

        public bool Invulnerable
        {
            get { return m_invulnerable; }
            set { m_invulnerable = value; }
        }

        /// <summary>
        /// The User Level the client has (taken from UserAccount)
        /// </summary>
        public int UserLevel
        {
            get { return m_userLevel; }
        }

        /// <summary>
        /// The current status of god level in the client
        /// </summary>
        public int GodLevel
        {
            get { return m_godLevel; }
            set { m_godLevel = value; }
        }

        public Vector3 CameraPosition
        {
            get { return m_CameraCenter; }
        }

        public Quaternion CameraRotation
        {
            get { return Util.Axes2Rot(m_CameraAtAxis, m_CameraLeftAxis, m_CameraUpAxis); }
        }

        public Vector3 CameraAtAxis
        {
            get { return m_CameraAtAxis; }
        }

        public Vector3 CameraLeftAxis
        {
            get { return m_CameraLeftAxis; }
        }

        public Vector3 CameraUpAxis
        {
            get { return m_CameraUpAxis; }
        }

        public Vector3 Lookat
        {
            get
            {
                Vector3 a = new Vector3(m_CameraAtAxis.X, m_CameraAtAxis.Y, 0);

                if (a == Vector3.Zero)
                    return a;

                return Util.GetNormalizedVector(a);
            }
        }

        private readonly string m_firstname;

        public string Firstname
        {
            get { return m_firstname; }
        }

        private readonly string m_lastname;

        public string Lastname
        {
            get { return m_lastname; }
        }

        public float DrawDistance
        {
            get { return m_DrawDistance; }
            set
            {
                if (m_DrawDistance != value)
                {
                    m_DrawDistance = value;
                    //Fire the event
                    Scene.AuroraEventManager.FireGenericEventHandler("DrawDistanceChanged", this);
                    if (!IsChildAgent)
                    {
                        //Send an update to all child agents if we are a root agent
                        m_enqueueSendChildAgentUpdate = true;
                        m_enqueueSendChildAgentUpdateTime = DateTime.Now.AddSeconds(10);
                    }
                }
            }
        }

        protected bool m_allowMovement = true;

        public bool AllowMovement
        {
            get { return m_allowMovement; }
            set { m_allowMovement = value; }
        }

        protected bool m_Frozen = false;

        public bool Frozen
        {
            get { return m_Frozen; }
            set { m_Frozen = value; }
        }

        protected bool m_IsJumping = false;
        public bool IsJumping
        {
            get { return m_IsJumping; }
            set { m_IsJumping = value; }
        }

        protected bool ClientIsStarting = true;

        public bool SetAlwaysRun
        {
            get
            {
                if (PhysicsActor != null)
                {
                    return PhysicsActor.SetAlwaysRun;
                }
                else
                {
                    return m_setAlwaysRun;
                }
            }
            set
            {
                m_setAlwaysRun = value;
                if (PhysicsActor != null)
                {
                    PhysicsActor.SetAlwaysRun = value;
                }
            }
        }

        public byte State
        {
            get { return m_state; }
            set { m_state = value; }
        }

        public uint AgentControlFlags
        {
            get { return (uint)m_AgentControlFlags; }
            set { m_AgentControlFlags = (AgentManager.ControlFlags)value; }
        }

        /// <summary>
        /// This works out to be the ClientView object associated with this avatar, or it's client connection manager
        /// </summary>
        private IClientAPI m_controllingClient;

        protected PhysicsActor m_physicsActor;

        /// <value>
        /// The client controlling this presence
        /// </value>
        public IClientAPI ControllingClient
        {
            get { return m_controllingClient; }
        }

        public IClientCore ClientView
        {
            get { return (IClientCore) m_controllingClient; }
        }

        protected Vector3 m_parentPosition;
        public Vector3 ParentPosition
        {
            get { return m_parentPosition; }
            set { m_parentPosition = value; }
        }

        /// <summary>
        /// Position of this avatar relative to the region the avatar is in
        /// </summary>
        public override Vector3 AbsolutePosition
        {
            get
            {
                PhysicsActor actor = m_physicsActor;
                if (actor != null)
                    m_pos = actor.Position;
                else
                {
                    //return m_pos; 
                    // OpenSim Mantis #4063. Obtain the correct position of a seated avatar. In addition
                    // to providing the correct position while the avatar is seated, this value will also
                    // be used as the location to unsit to.
                    //
                    // If m_parentID is not 0, assume we are a seated avatar and we should return the
                    // position based on the sittarget offset and rotation of the prim we are seated on.
                    //
                    // Generally, m_pos will contain the position of the avator in the sim unless the avatar
                    // is on a sit target. While on a sit target, m_pos will contain the desired offset
                    // without the parent rotation applied.
                    if (m_parentID != UUID.Zero)
                    {
                        SceneObjectPart part = m_scene.GetSceneObjectPart(m_parentID);
                        if (part != null)
                        {
                            return m_parentPosition + (m_pos * part.GetWorldRotation());
                        }
                        else
                        {
                            return m_parentPosition + m_pos;
                        }
                    }
                }

                return m_pos;  
            }
            set
            {
                PhysicsActor actor = m_physicsActor;
                if (actor != null)
                {
                    try
                    {
                        lock (m_scene.SyncRoot)
                        {
                            if (m_physicsActor != null)
                            {
                                m_physicsActor.Position = value;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[SCENEPRESENCE]: ABSOLUTE POSITION " + e.ToString());
                    }
                }

                m_pos = value;
                m_parentPosition = Vector3.Zero;
            }
        }

        public Vector3 OffsetPosition
        {
            get { return m_pos; }
            set { m_pos = value; }
        }

        /// <summary>
        /// Current velocity of the avatar.
        /// </summary>
        public override Vector3 Velocity
        {
            get
            {
                PhysicsActor actor = m_physicsActor;
                if (actor != null)
                    return actor.Velocity;

                return Vector3.Zero;
            }
            set
            {
                PhysicsActor actor = m_physicsActor;
                if (actor != null)
                {
                    try
                    {
                        lock (m_scene.SyncRoot)
                            actor.Velocity = value;
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[SCENEPRESENCE]: VELOCITY " + e.Message);
                    }
                }
            }
        }

        public Quaternion Rotation
        {
            get { return m_bodyRot; }
            set { m_bodyRot = value; }
        }

        /// <summary>
        /// If this is true, agent doesn't have a representation in this scene.
        ///    this is an agent 'looking into' this scene from a nearby scene(region)
        ///
        /// if False, this agent has a representation in this scene
        /// </summary>
        private bool m_isChildAgent = true;

        public bool IsChildAgent
        {
            get { return m_isChildAgent; }
            set { m_isChildAgent = value; }
        }

        private UUID m_parentID;

        public UUID ParentID
        {
            get { return m_parentID; }
            set { m_parentID = value; }
        }

        public ISceneViewer SceneViewer
        {
            get { return m_sceneViewer; }
        }

        private bool m_inTransit;
        private bool m_mouseLook;
        private bool m_leftButtonDown;
        private bool m_isAway = false;
        private bool m_isBusy = false;

        public bool IsInTransit
        {
            get { return m_inTransit; }
            set { m_inTransit = value; }
        }

        public bool IsAway
        {
            get { return m_isAway; }
            set { m_isAway = value; }
        }

        public bool IsBusy
        {
            get { return m_isBusy; }
            set { m_isBusy = value; }
        }

        public float SpeedModifier
        {
            get { return m_speedModifier; }
            set { m_speedModifier = value; }
        }

        public bool ForceFly
        {
            get { return m_forceFly; }
            set { m_forceFly = value; }
        }

        public bool FlyDisabled
        {
            get { return m_flyDisabled; }
            set { m_flyDisabled = value; }
        }

        #endregion

        #region Constructor(s)
        
        public ScenePresence()
        {
            m_sendCourseLocationsMethod = SendCoarseLocationsDefault;
        }

        public ScenePresence(IClientAPI client, Scene world, RegionInfo reginfo)
            : this()
        {
            m_controllingClient = client;
            m_firstname = m_controllingClient.FirstName;
            m_lastname = m_controllingClient.LastName;
            m_name = String.Format("{0} {1}", m_firstname, m_lastname);
            m_scene = world;
            m_uuid = client.AgentId;
            m_regionInfo = reginfo;
            m_localId = m_scene.SceneGraph.AllocateLocalId();

            CreateSceneViewer();

            m_animator = new ScenePresenceAnimator(this);

            UserAccount account = m_scene.UserAccountService.GetUserAccount(m_scene.RegionInfo.ScopeID, m_uuid);

            if (account != null)
                m_userLevel = account.UserLevel;

            AbsolutePosition = posLastSignificantMove = m_CameraCenter = m_controllingClient.StartPos;

            // This won't send anything, as we are still a child here...
            //Animator.TrySetMovementAnimation("STAND"); 

            // we created a new ScenePresence (a new child agent) in a fresh region.
            // Request info about all the (root) agents in this region
            // Note: This won't send data *to* other clients in that region (children don't send)
            //SendInitialFullUpdateToAllClients();
            SendOtherAgentsAvatarDataToMe();
            //Comment this out for now, just to see what happens
            //SendOtherAgentsAppearanceToMe();

            RegisterToEvents();
            SetDirectionVectors();
        }

        public ScenePresence(IClientAPI client, Scene world, RegionInfo reginfo, AvatarAppearance appearance)
            : this(client, world, reginfo)
        {
            m_appearance = appearance;
        }

        private void CreateSceneViewer()
        {
            m_sceneViewer = new SceneViewer(this);
        }

        public void RegisterToEvents()
        {
            m_controllingClient.OnCompleteMovementToRegion += CompleteMovement;
            m_controllingClient.OnAgentUpdate += HandleAgentUpdate;
            m_controllingClient.OnAgentRequestSit += HandleAgentRequestSit;
            m_controllingClient.OnAgentSit += HandleAgentSit;
            m_controllingClient.OnSetAlwaysRun += HandleSetAlwaysRun;
            m_controllingClient.OnStartAnim += HandleStartAnim;
            m_controllingClient.OnStopAnim += HandleStopAnim;
            m_controllingClient.OnForceReleaseControls += HandleForceReleaseControls;
            m_controllingClient.OnAutoPilotGo += DoAutoPilot;
            m_controllingClient.AddGenericPacketHandler("autopilot", DoMoveToPosition);
            m_controllingClient.OnRegionHandleRequest += RegionHandleRequest;
            m_controllingClient.OnNameFromUUIDRequest += HandleUUIDNameRequest;
        }

        private void SetDirectionVectors()
        {
            Dir_Vectors[0] = Vector3.UnitX; //FORWARD
            Dir_Vectors[1] = -Vector3.UnitX; //BACK
            Dir_Vectors[2] = Vector3.UnitY; //LEFT
            Dir_Vectors[3] = -Vector3.UnitY; //RIGHT
            Dir_Vectors[4] = Vector3.UnitZ; //UP
            Dir_Vectors[5] = -Vector3.UnitZ; //DOWN
            Dir_Vectors[8] = new Vector3(0f, 0f, -0.5f); //DOWN_Nudge
            Dir_Vectors[6] = Vector3.UnitX*2; //FORWARD
            Dir_Vectors[7] = -Vector3.UnitX; //BACK
            Dir_Vectors[9] = new Vector3(0, 4, 0); //LEFT Nudge
            Dir_Vectors[10] = new Vector3(0, -4, 0); //RIGHT Nudge
        }

        private Vector3[] GetWalkDirectionVectors()
        {
            Vector3[] vector = new Vector3[11];
            vector[0] = new Vector3(m_CameraUpAxis.Z, 0f, -m_CameraAtAxis.Z); //FORWARD
            vector[1] = new Vector3(-m_CameraUpAxis.Z, 0f, m_CameraAtAxis.Z); //BACK
            vector[2] = Vector3.UnitY; //LEFT
            vector[3] = -Vector3.UnitY; //RIGHT
            vector[4] = new Vector3(m_CameraAtAxis.Z, 0f, m_CameraUpAxis.Z); //UP
            vector[5] = new Vector3(-m_CameraAtAxis.Z, 0f, -m_CameraUpAxis.Z); //DOWN
            vector[8] = new Vector3(-m_CameraAtAxis.Z, 0f, -m_CameraUpAxis.Z); //DOWN_Nudge
            vector[6] = (new Vector3(m_CameraUpAxis.Z, 0f, -m_CameraAtAxis.Z) * 2); //FORWARD Nudge
            vector[7] = new Vector3(-m_CameraUpAxis.Z, 0f, m_CameraAtAxis.Z); //BACK Nudge
            vector[9] = new Vector3(0, 2, 0); //LEFT Nudge
            vector[10] = new Vector3(0, -2, 0); //RIGHT Nudge
            return vector;
        }

        #endregion

        public uint GenerateClientFlags(SceneObjectPart part)
        {
            return m_scene.Permissions.GenerateClientFlags(m_uuid, part);
        }

        #region Status Methods

        /// <summary>
        /// This turns a child agent, into a root agent
        /// This is called when an agent teleports into a region, or if an
        /// agent crosses into this region from a neighbor over the border
        /// </summary>
        public void MakeRootAgent(bool isFlying)
        {
            m_log.DebugFormat(
                "[SCENE]: Upgrading child to root agent for {0} in {1}",
                Name, m_scene.RegionInfo.RegionName);

            AddToPhysicalScene(isFlying, false);

            if (m_forceFly)
                m_physicsActor.Flying = true;
            else if (m_flyDisabled)
                m_physicsActor.Flying = false;

            // Don't send an animation pack here, since on a region crossing this will sometimes cause a flying 
            // avatar to return to the standing position in mid-air.  On login it looks like this is being sent
            // elsewhere anyway
            // Animator.SendAnimPack();

            // On the next prim update, all objects will be sent
            //
            m_sceneViewer.Reset();

            IsChildAgent = false;

            // send the animations of the other presences to me
            m_scene.ForEachScenePresence(delegate(ScenePresence presence)
            {
                if (presence != this)
                    presence.Animator.SendAnimPackToClient(ControllingClient);
            });

            IAttachmentsModule attMod = Scene.RequestModuleInterface<IAttachmentsModule>();
            if (attMod != null)
                attMod.SendScriptEventToAttachments(UUID, "changed", new Object[] { Changed.TELEPORT });

            m_scene.EventManager.TriggerOnMakeRootAgent(this);

            //Check to make sure that we have sent all the appearance info 10 seconds later
            Timer t = new Timer(10 * 1000);
            t.Elapsed += CheckToMakeSureWearablesHaveBeenSent;
            t.AutoReset = false;
            t.Start();
        }

        /// <summary>
        /// This makes sure that after the agent has entered the sim that they have their clothes and that they all exist
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void CheckToMakeSureWearablesHaveBeenSent(object sender, ElapsedEventArgs e)
        {
            if (!m_InitialHasWearablesBeenSent)
            {
                m_InitialHasWearablesBeenSent = true;
                m_log.Warn("[ScenePresence]: Been 10 seconds since root agent " + Name + " was added and appearance was not sent, force sending now.");

                //Force send!
                m_InitialHasWearablesBeenSent = true;

                ControllingClient.SendWearables(Appearance.Wearables, Appearance.Serial);

                //Send rebakes if needed
                // NOTE: Do NOT send this! It seems to make the client become a cloud
                //sp.SendAppearanceToAgent(sp);

                // If the avatars baked textures are all in the cache, then we have a 
                // complete appearance... send it out, if not, then we'll send it when
                // the avatar finishes updating its appearance
                SendAppearanceToAllOtherAgents();

                // This agent just became roo t. We are going to tell everyone about it. The process of
                // getting other avatars information was initiated in the constructor... don't do it 
                // again here... 
                SendAvatarDataToAllAgents();

                //Tell us about everyone else as well now that we are here
                SendOtherAgentsAppearanceToMe();
            }
        }

        /// <summary>
        /// This turns a root agent into a child agent
        /// when an agent departs this region for a neighbor, this gets called.
        ///
        /// It doesn't get called for a teleport.  Reason being, an agent that
        /// teleports out may not end up anywhere near this region
        /// </summary>
        public void MakeChildAgent()
        {
            // It looks like m_animator is set to null somewhere, and MakeChild
            // is called after that. Probably in aborted teleports.
            if (m_animator == null)
                m_animator = new ScenePresenceAnimator(this);
            else
                Animator.ResetAnimations();


//            m_log.DebugFormat(
//                 "[SCENEPRESENCE]: Downgrading root agent {0}, {1} to a child agent in {2}",
//                 Name, UUID, m_scene.RegionInfo.RegionName);

            // Don't zero out the velocity since this can cause problems when an avatar is making a region crossing,
            // depending on the exact timing.  This shouldn't matter anyway since child agent positions are not updated.
            //Velocity = new Vector3(0, 0, 0);

            IsChildAgent = true;
            RemoveFromPhysicalScene();

            // FIXME: Set m_rootRegionHandle to the region handle of the scene this agent is moving into

            IAttachmentsModule attMod = Scene.RequestModuleInterface<IAttachmentsModule>();
            if (attMod != null)
                attMod.SendScriptEventToAttachments(UUID, "changed", new Object[] { Changed.TELEPORT });
            m_scene.EventManager.TriggerOnMakeChildAgent(this);

            Reset();
        }

        public delegate void RemovePhysics();
        public event RemovePhysics OnRemovePhysics;

        /// <summary>
        /// Removes physics plugin scene representation of this agent if it exists.
        /// </summary>
        private void RemoveFromPhysicalScene()
        {
            try
            {
                if (PhysicsActor != null)
                {
                    if (OnRemovePhysics != null)
                        OnRemovePhysics();
                    if (m_physicsActor != null)
                        m_scene.SceneGraph.PhysicsScene.RemoveAvatar(m_physicsActor);
                    if (m_physicsActor != null)
                        m_physicsActor.OnCollisionUpdate -= PhysicsCollisionUpdate;
                    if (m_physicsActor != null)
                        m_physicsActor.OnRequestTerseUpdate -= SendTerseUpdateToAllClients;
                    if (m_physicsActor != null)
                        m_physicsActor.OnSignificantMovement -= CheckForSignificantMovement;
                    if (m_physicsActor != null)
                        m_physicsActor.OnPositionAndVelocityUpdate -= PhysicsUpdatePosAndVelocity;
                    if (m_physicsActor != null)
                        m_physicsActor.OnOutOfBounds -= OutOfBoundsCall;
                    if (m_physicsActor != null)
                        m_scene.SceneGraph.PhysicsScene.RemoveAvatar(PhysicsActor);
                    if (m_physicsActor != null)
                        m_physicsActor.UnSubscribeEvents();
                    m_physicsActor = null;
                }
            }
            catch { }
        }

        public void Teleport(Vector3 pos)
        {
            m_overrideUserInput = false;
            m_endForceTime = 0;
            bool isFlying = false;
            if (m_physicsActor != null)
                isFlying = m_physicsActor.Flying;
            
            RemoveFromPhysicalScene();
            Velocity = Vector3.Zero;
            AbsolutePosition = pos;
            AddToPhysicalScene(isFlying, true);
            if (m_appearance != null)
            {
                if (m_appearance.AvatarHeight > 0)
                    SetHeight(m_appearance.AvatarHeight);
            }

            IAttachmentsModule attMod = Scene.RequestModuleInterface<IAttachmentsModule>();
            if (attMod != null)
                attMod.SendScriptEventToAttachments(UUID, "changed", new Object[] { Changed.TELEPORT });
            SendTerseUpdateToAllClients();
        }

        public void TeleportWithMomentum(Vector3 pos)
        {
            m_overrideUserInput = false;
            m_endForceTime = 0;
            bool isFlying = false;
            if (m_physicsActor != null)
                isFlying = m_physicsActor.Flying;

            RemoveFromPhysicalScene();
            AbsolutePosition = pos;
            AddToPhysicalScene(isFlying, true);
            if (m_appearance != null)
            {
                if (m_appearance.AvatarHeight > 0)
                    SetHeight(m_appearance.AvatarHeight);
            }

            IAttachmentsModule attMod = Scene.RequestModuleInterface<IAttachmentsModule>();
            if (attMod != null)
                attMod.SendScriptEventToAttachments(UUID, "changed", new Object[] { Changed.TELEPORT });
            SendTerseUpdateToAllClients();
        }

        public void StopFlying()
        {
            ControllingClient.StopFlying(this);
        }

        #endregion

        #region Event Handlers

        public void HandleUUIDNameRequest(UUID uuid, IClientAPI remote_client)
        {
            UserAccount account = m_scene.UserAccountService.GetUserAccount(m_scene.RegionInfo.ScopeID, uuid);
            if (account != null)
            {
                remote_client.SendNameReply(uuid, account.FirstName, account.LastName);
            }
        }

        public void RegionHandleRequest(IClientAPI client, UUID regionID)
        {
            ulong handle = 0;
            if (regionID == m_scene.RegionInfo.RegionID)
                handle = m_scene.RegionInfo.RegionHandle;
            else
            {
                GridRegion r = m_scene.GridService.GetRegionByUUID(UUID.Zero, regionID);
                if (r != null)
                    handle = r.RegionHandle;
            }

            if (handle != 0)
                client.SendRegionHandle(regionID, handle);
        }

        /// <summary>
        /// Sets avatar height in the phyiscs plugin
        /// </summary>
        public void SetHeight(float height)
        {
            //If the av exists, set their new size, if not, add them to the region
            if (PhysicsActor != null && !IsChildAgent)
            {
                if (height != m_avHeight)
                {
                    Vector3 SetSize = new Vector3(0.45f, 0.6f, Appearance.AvatarHeight);
                    PhysicsActor.Size = SetSize;
                }
            }
            m_avHeight = height;
        }

        /// <summary>
        /// Complete Avatar's movement into the region.
        /// This is called upon a very important packet sent from the client,
        /// so it's client-controlled. Never call this method directly.
        /// </summary>
        private void CompleteMovement(IClientAPI client)
        {
            //m_log.Debug("[SCENE PRESENCE]: CompleteMovement for " + Name + " in " + m_regionInfo.RegionName);

            string reason;
            Vector3 pos;
            //Get a good position and make sure that we exist in the grid
            if (!Scene.Permissions.AllowedIncomingTeleport(UUID, AbsolutePosition, out pos, out reason))
            {
                m_log.Error("[ScenePresence]: Error in MakeRootAgent! Could not authorize agent " + Name +
                    ", reason: " + reason);
                return;
            }

            AbsolutePosition = pos;

            Vector3 look = Velocity;
            if (look == Vector3.Zero)
                look = new Vector3(0.99f, 0.042f, 0);

            //Put the agent in an allowed area and above the terrain.
            IParcelManagementModule parcelManagement = RequestModuleInterface<IParcelManagementModule>();
            if (parcelManagement != null)
                AbsolutePosition = parcelManagement.GetNearestAllowedPosition(this);

            IsChildAgent = false;
            
            //Do this and SendInitialData FIRST before MakeRootAgent to try to get the updates to the client out so that appearance loads better
            m_controllingClient.MoveAgentIntoRegion(m_regionInfo, AbsolutePosition, look);

            //Send updates to everyone about us
            SendAvatarDataToAllAgents();

            bool m_flying = ((m_AgentControlFlags & AgentManager.ControlFlags.AGENT_CONTROL_FLY) != 0);
            MakeRootAgent(m_flying);

            //Tell the grid that we successfully got here

            AgentCircuitData agent = ControllingClient.RequestClientInfo();
            agent.startpos = AbsolutePosition;
            agent.child = true;
            agent.Appearance = Appearance;

            ISyncMessagePosterService syncPoster = Scene.RequestModuleInterface<ISyncMessagePosterService>();
            if (syncPoster != null)
                syncPoster.Post(SyncMessageHelper.ArrivedAtDestination(UUID, (int)DrawDistance, agent, Scene.RegionInfo.RegionHandle));

            IFriendsModule friendsModule = m_scene.RequestModuleInterface<IFriendsModule>();
            if (friendsModule != null)
                friendsModule.SendFriendsOnlineIfNeeded(ControllingClient);
        }

        /// <summary>
        /// Callback for the Camera view block check.  Gets called with the results of the camera view block test
        /// hitYN is true when there's something in the way.
        /// </summary>
        /// <param name="hitYN"></param>
        /// <param name="collisionPoint"></param>
        /// <param name="localid"></param>
        /// <param name="distance"></param>
        public void RayCastCameraCallback(bool hitYN, Vector3 collisionPoint, uint localid, float distance, Vector3 pNormal)
        {
            if (m_followCamAuto)
            {
                if (hitYN)
                {
                    CameraConstraintActive = true;
                    //m_log.DebugFormat("[RAYCASTRESULT]: {0}, {1}, {2}, {3}", hitYN, collisionPoint, localid, distance);

                    Vector3 normal = Vector3.Normalize(new Vector3(0f, 0f, collisionPoint.Z) - collisionPoint);
                    ControllingClient.SendCameraConstraint(new Vector4(normal.X, normal.Y, normal.Z, -1 * Vector3.Distance(new Vector3(0, 0, collisionPoint.Z), collisionPoint)));
                }
                else
                {
                    if (CameraConstraintActive)
                    {
                        ControllingClient.SendCameraConstraint(new Vector4(0f, 0.5f, 0.9f, -3000f));
                        CameraConstraintActive = false;
                    }
                }
            }
        }

        /// <summary>
        /// This is the event handler for client movement. If a client is moving, this event is triggering.
        /// </summary>
        public void HandleAgentUpdate(IClientAPI remoteClient, AgentUpdateArgs agentData)
        {
            m_perfMonMS = Util.EnvironmentTickCount();

            ++m_movementUpdateCount;
            if (m_movementUpdateCount < 1)
                m_movementUpdateCount = 1;

            #region Sanity Checking

            // This is irritating.  Really.
            if (!AbsolutePosition.IsFinite())
            {
                RemoveFromPhysicalScene();
                m_log.Error("[AVATAR]: NonFinite Avatar position detected... Reset Position. Mantis this please. Error #9999902");

                m_pos = new Vector3(m_scene.RegionInfo.RegionSizeX / 2, m_scene.RegionInfo.RegionSizeY / 2,
                    128);
                //Make them fly so that they don't just fall
                AddToPhysicalScene(true, false);
            }

            #endregion Sanity Checking

            #region Inputs

            AgentManager.ControlFlags flags = (AgentManager.ControlFlags)agentData.ControlFlags;
            Quaternion bodyRotation = agentData.BodyRotation;

            //Check to see whether ray casting needs done
            // We multiply by 10 so that we don't trigger it when the camera moves slightly (as its 2 meter change)
            if (Util.GetFlatDistanceTo(agentData.CameraCenter, m_lastCameraCenter) > SIGNIFICANT_MOVEMENT * 10)
            {
                m_lastCameraCenter = agentData.CameraCenter;
                Scene.AuroraEventManager.FireGenericEventHandler("SignficantCameraMovement", this);
            }

            // Camera location in world.  We'll need to raytrace
            // from this location from time to time.
            m_CameraCenter = agentData.CameraCenter;

            // Use these three vectors to figure out what the agent is looking at
            // Convert it to a Matrix and/or Quaternion
            m_CameraAtAxis = agentData.CameraAtAxis;
            m_CameraLeftAxis = agentData.CameraLeftAxis;
            m_CameraUpAxis = agentData.CameraUpAxis;
            // The Agent's Draw distance setting
            DrawDistance = agentData.Far;

            // Check if Client has camera in 'follow cam' or 'build' mode.
            Vector3 camdif = (Vector3.One * m_bodyRot - Vector3.One * CameraRotation);

            m_followCamAuto = ((m_CameraUpAxis.Z > 0.959f && m_CameraUpAxis.Z < 0.98f)
               && (Math.Abs(camdif.X) < 0.4f && Math.Abs(camdif.Y) < 0.4f)) ? true : false;

            m_mouseLook = (flags & AgentManager.ControlFlags.AGENT_CONTROL_MOUSELOOK) != 0;
            m_leftButtonDown = (flags & AgentManager.ControlFlags.AGENT_CONTROL_LBUTTON_DOWN) != 0;
            m_isAway = (flags & AgentManager.ControlFlags.AGENT_CONTROL_AWAY) != 0;
            #endregion Inputs

            if ((flags & AgentManager.ControlFlags.AGENT_CONTROL_STAND_UP) != 0)
            {
                StandUp();
            }

            //m_log.DebugFormat("[FollowCam]: {0}", m_followCamAuto);
            // Raycast from the avatar's head to the camera to see if there's anything blocking the view
            if ((m_movementUpdateCount % NumMovementsBetweenRayCast) == 0 && m_scene.SceneGraph.PhysicsScene.SupportsRayCast())
            {
                if (m_followCamAuto)
                {
                    Vector3 posAdjusted = m_pos + HEAD_ADJUSTMENT;
                    m_scene.SceneGraph.PhysicsScene.RaycastWorld(m_pos, Vector3.Normalize(m_CameraCenter - posAdjusted), Vector3.Distance(m_CameraCenter, posAdjusted) + 0.3f, RayCastCameraCallback);
                }
            }
            if (!m_CameraCenter.IsFinite())
            {
                m_CameraCenter = new Vector3(128, 128, 128);
            }

            lock (scriptedcontrols)
            {
                if (scriptedcontrols.Count > 0)
                {
                    SendControlToScripts((uint)flags);
                    flags = RemoveIgnoredControls(flags, IgnoredControls);
                }
            }

            if (m_autopilotMoving)
                CheckAtSitTarget();

            // In the future, these values might need to go global.
            // Here's where you get them.
            if (!SitGround)
                SitGround = (flags & AgentManager.ControlFlags.AGENT_CONTROL_SIT_ON_GROUND) != 0;
            m_AgentControlFlags = flags;
            m_headrotation = agentData.HeadRotation;
            m_state = agentData.State;

            PhysicsActor actor = PhysicsActor;
            if (actor == null)
            {
                m_log.Debug("Null physical actor in AgentUpdate in " + m_scene.RegionInfo.RegionName);
                return;
            }
            
            bool update_movementflag = false;

            if (AllowMovement && !SitGround && !Frozen)
            {
                //! Note: we use last absolute position instead of camera pos as camera pos is updated far too often.
                //! If you put your mouse on an object, it would reprioritize updates for over at the objects position, then again when you lifted the mouse off of the object if the object is more than 10 meters away.
                //! So we only do AbsolutePosition for now until we can find a better way.
                if (Vector3.Distance(m_lastAbsolutePosition, AbsolutePosition) >= m_sceneViewer.Prioritizer.RootReprioritizationDistance)
                {
                    m_lastAbsolutePosition = AbsolutePosition;
                    m_sceneViewer.Reprioritize();
                }

                if (agentData.UseClientAgentPosition)
                {
                    m_moveToPositionInProgress = (agentData.ClientAgentPosition - AbsolutePosition).Length() > 0.2f;
                    m_moveToPositionTarget = agentData.ClientAgentPosition;
                }

                int i = 0;
                
                bool update_rotation = false;
                bool DCFlagKeyPressed = false;
                Vector3 agent_control_v3 = Vector3.Zero;
                Quaternion q = bodyRotation;

                bool oldflying = PhysicsActor.Flying;

                if (m_forceFly)
                    actor.Flying = true;
                else if (m_flyDisabled)
                    actor.Flying = false;
                else
                    actor.Flying = ((flags & AgentManager.ControlFlags.AGENT_CONTROL_FLY) != 0);

                if (actor.Flying != oldflying)
                    update_movementflag = true;

                if (q != m_bodyRot)
                {
                    m_bodyRot = q;
                    update_rotation = true;
                }

                if (m_parentID == UUID.Zero)
                {
                    bool bAllowUpdateMoveToPosition = false;
                    bool bResetMoveToPosition = false;

                    Vector3[] dirVectors;

                    // use camera up angle when in mouselook and not flying or when holding the left mouse button down and not flying
                    // this prevents 'jumping' in inappropriate situations.
                    if ((m_mouseLook && !m_physicsActor.Flying) || (m_leftButtonDown && !m_physicsActor.Flying))
                        dirVectors = GetWalkDirectionVectors();
                    else
                        dirVectors = Dir_Vectors;

                    // The fact that m_movementflag is a byte needs to be fixed
                    // it really should be a uint
                    uint nudgehack = 250;
                    //Do these two like this to block out all others because it will slow it down
                    if ((flags & AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_POS) == AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_POS)
                    {
                        bResetMoveToPosition = true;
                        DCFlagKeyPressed = true;
                        agent_control_v3 += dirVectors[9];
                    }
                    else if ((flags & AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_NEG) == AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_NEG)
                    {
                        bResetMoveToPosition = true;
                        DCFlagKeyPressed = true;
                        agent_control_v3 += dirVectors[10];
                    }
                    else
                    {
                        foreach (Dir_ControlFlags DCF in DIR_CONTROL_FLAGS)
                        {
                            if (((uint)flags & (uint)DCF) != 0)
                            {
                                bResetMoveToPosition = true;
                                DCFlagKeyPressed = true;
                                agent_control_v3 += dirVectors[i];
                                //m_log.DebugFormat("[Motion]: {0}, {1}",i, dirVectors[i]);

                                if ((m_movementflag & (uint)DCF) == 0)
                                {
                                    if (DCF == Dir_ControlFlags.DIR_CONTROL_FLAG_FORWARD_NUDGE || DCF == Dir_ControlFlags.DIR_CONTROL_FLAG_BACKWARD_NUDGE)
                                    {
                                        m_movementflag |= (byte)nudgehack;
                                    }
                                    m_movementflag += (uint)DCF;
                                    update_movementflag = true;
                                }
                            }
                            else
                            {
                                if ((m_movementflag & (uint)DCF) != 0 ||
                                    ((DCF == Dir_ControlFlags.DIR_CONTROL_FLAG_FORWARD_NUDGE || DCF == Dir_ControlFlags.DIR_CONTROL_FLAG_BACKWARD_NUDGE)
                                    && ((m_movementflag & nudgehack) == nudgehack))
                                    ) // This or is for Nudge forward
                                {
                                    m_movementflag -= ((uint)DCF);

                                    update_movementflag = true;
                                    /*
                                        if ((DCF == Dir_ControlFlags.DIR_CONTROL_FLAG_FORWARD_NUDGE || DCF == Dir_ControlFlags.DIR_CONTROL_FLAG_BACKWARD_NUDGE)
                                        && ((m_movementflag & (byte)nudgehack) == nudgehack))
                                        {
                                            m_log.Debug("Removed Hack flag");
                                        }
                                    */
                                }
                                else
                                {
                                    bAllowUpdateMoveToPosition = true;
                                }
                            }
                            i++;
                        }
                    }

                    //Paupaw:Do Proper PID for Autopilot here
                    if (bResetMoveToPosition)
                    {
                        m_moveToPositionTarget = Vector3.Zero;
                        m_moveToPositionInProgress = false;
                        update_movementflag = true;
                        bAllowUpdateMoveToPosition = false;
                    }

                    if (bAllowUpdateMoveToPosition && (m_moveToPositionInProgress && !m_autopilotMoving))
                    {
                        //Check the error term of the current position in relation to the target position
                        if (Util.GetFlatDistanceTo(AbsolutePosition, m_moveToPositionTarget) <= 0.5f)
                        {
                            // we are close enough to the target
                            m_moveToPositionTarget = Vector3.Zero;
                            m_moveToPositionInProgress = false;
                            update_movementflag = true;
                        }
                        else
                        {
                            try
                            {
                                // move avatar in 2D at one meter/second towards target, in avatar coordinate frame.
                                // This movement vector gets added to the velocity through AddNewMovement().
                                // Theoretically we might need a more complex PID approach here if other 
                                // unknown forces are acting on the avatar and we need to adaptively respond
                                // to such forces, but the following simple approach seems to works fine.
                                Vector3 LocalVectorToTarget3D =
                                    (m_moveToPositionTarget - AbsolutePosition) // vector from cur. pos to target in global coords
                                    * Matrix4.CreateFromQuaternion(Quaternion.Inverse(bodyRotation)); // change to avatar coords
                                // Ignore z component of vector
                                Vector3 LocalVectorToTarget2D = new Vector3((float)(LocalVectorToTarget3D.X), (float)(LocalVectorToTarget3D.Y), 0f);
                                LocalVectorToTarget2D.Normalize();
                                agent_control_v3 += LocalVectorToTarget2D;

                                // update avatar movement flags. the avatar coordinate system is as follows:
                                //
                                //                        +X (forward)
                                //
                                //                        ^
                                //                        |
                                //                        |
                                //                        |
                                //                        |
                                //     (left) +Y <--------o--------> -Y
                                //                       avatar
                                //                        |
                                //                        |
                                //                        |
                                //                        |
                                //                        v
                                //                        -X
                                //

                                // based on the above avatar coordinate system, classify the movement into 
                                // one of left/right/back/forward.
                                if (LocalVectorToTarget2D.Y > 0)//MoveLeft
                                {
                                    m_movementflag += (uint)Dir_ControlFlags.DIR_CONTROL_FLAG_LEFT;
                                    //AgentControlFlags
                                    AgentControlFlags |= (uint)Dir_ControlFlags.DIR_CONTROL_FLAG_LEFT;
                                    update_movementflag = true;
                                }
                                else if (LocalVectorToTarget2D.Y < 0) //MoveRight
                                {
                                    m_movementflag += (uint)Dir_ControlFlags.DIR_CONTROL_FLAG_RIGHT;
                                    AgentControlFlags |= (uint)Dir_ControlFlags.DIR_CONTROL_FLAG_RIGHT;
                                    update_movementflag = true;
                                }
                                if (LocalVectorToTarget2D.X < 0) //MoveBack
                                {
                                    m_movementflag += (uint)Dir_ControlFlags.DIR_CONTROL_FLAG_BACK;
                                    AgentControlFlags |= (uint)Dir_ControlFlags.DIR_CONTROL_FLAG_BACK;
                                    update_movementflag = true;
                                }
                                else if (LocalVectorToTarget2D.X > 0) //Move Forward
                                {
                                    m_movementflag += (uint)Dir_ControlFlags.DIR_CONTROL_FLAG_FORWARD;
                                    AgentControlFlags |= (uint)Dir_ControlFlags.DIR_CONTROL_FLAG_FORWARD;
                                    update_movementflag = true;
                                }
                            }
                            catch (Exception e)
                            {
                                //Avoid system crash, can be slower but...
                                m_log.DebugFormat("Crash! {0}", e.ToString());
                            }
                        }
                    }
                }

                // Cause the avatar to stop flying if it's colliding
                // with something with the down arrow pressed.

                // Only do this if we're flying
                if (m_physicsActor != null && m_physicsActor.Flying && !m_forceFly)
                {
                    // Landing detection code

                    // Are the landing controls requirements filled?
                    bool controlland = (((flags & AgentManager.ControlFlags.AGENT_CONTROL_UP_NEG) != 0) ||
                                        ((flags & AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_NEG) != 0));

                    // Are the collision requirements fulfilled?
                    bool colliding = (m_physicsActor.IsColliding == true);

                    if (m_physicsActor.Flying && colliding && controlland)
                    {
                        // nesting this check because LengthSquared() is expensive and we don't 
                        // want to do it every step when flying.
                        //The == Zero and Z > 0.1 are to stop people from flying and then falling down because the physics engine hasn't calculted the push yet
                        if (Velocity != Vector3.Zero && Math.Abs(Velocity.Z) > 0.15 && (Velocity.LengthSquared() <= LAND_VELOCITYMAG_MAX))
                            StopFlying();
                    }
                }

                // If the agent update does move the avatar, then calculate the force ready for the velocity update,
                // which occurs later in the main scene loop
                if (update_movementflag || (update_rotation && DCFlagKeyPressed))
                {
                    //                    m_log.DebugFormat("{0} {1}", update_movementflag, (update_rotation && DCFlagKeyPressed));
                    //                    m_log.DebugFormat(
                    //                        "In {0} adding velocity to {1} of {2}", m_scene.RegionInfo.RegionName, Name, agent_control_v3);

                    AddNewMovement(agent_control_v3, q);

                    m_scene.EventManager.TriggerOnClientMovement(this);
                }
            }

            //if (update_movementflag && ((flags & AgentManager.ControlFlags.AGENT_CONTROL_SIT_ON_GROUND) == 0) && (m_parentID == UUID.Zero) && !SitGround)
            //    Animator.UpdateMovementAnimations();

            IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
            if (reporter != null)
                reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(m_perfMonMS));
        }

        public void DoAutoPilot(uint not_used, Vector3 Pos, IClientAPI remote_client)
        {
            m_autopilotMoving = true;
            m_autoPilotTarget = Pos;
            m_sitAtAutoTarget = false;
            PrimitiveBaseShape proxy = PrimitiveBaseShape.Default;
            //proxy.PCode = (byte)PCode.ParticleSystem;

            proxyObjectGroup = new SceneObjectGroup(UUID, Pos, Rotation, proxy, m_scene);
            proxyObjectGroup.AttachToScene(m_scene);

            // Commented out this code since it could never have executed, but might still be informative.
            //            if (proxyObjectGroup != null)
            //            {
            proxyObjectGroup.ScheduleGroupUpdate(PrimUpdateFlags.FullUpdate);
            remote_client.SendSitResponse(proxyObjectGroup.UUID, Vector3.Zero, Quaternion.Identity, true, Vector3.Zero, Vector3.Zero, false);
            IBackupModule backup = m_scene.RequestModuleInterface<IBackupModule>();
            if (backup != null)
                backup.DeleteSceneObjects(new SceneObjectGroup[1] { proxyObjectGroup }, true);
            //            }
            //            else
            //            {
            //                m_autopilotMoving = false;
            //                m_autoPilotTarget = Vector3.Zero;
            //                ControllingClient.SendAlertMessage("Autopilot cancelled");
            //            }
        }

        public void DoMoveToPosition(Object sender, string method, List<String> args)
        {
            try
            {
                float locx = 0f;
                float locy = 0f;
                float locz = 0f;
                uint regionX = 0;
                uint regionY = 0;
                try
                {
                    Utils.LongToUInts(Scene.RegionInfo.RegionHandle, out regionX, out regionY);
                    locx = Convert.ToSingle(args[0]) - (float)regionX;
                    locy = Convert.ToSingle(args[1]) - (float)regionY;
                    locz = Convert.ToSingle(args[2]);
                }
                catch (InvalidCastException)
                {
                    m_log.Error("[CLIENT]: Invalid autopilot request");
                    return;
                }
                m_moveToPositionInProgress = true;
                m_moveToPositionTarget = new Vector3(locx, locy, locz);
                m_log.Warn("Moving to " + m_moveToPositionTarget);
            }
            catch (Exception ex)
            {
                //Why did I get this error?
               m_log.Error("[SCENEPRESENCE]: DoMoveToPosition" + ex);
            }
        }

        private void CheckAtSitTarget()
        {
            //m_log.Debug("[AUTOPILOT]: " + Util.GetDistanceTo(AbsolutePosition, m_autoPilotTarget).ToString());
            if (Util.GetDistanceTo(AbsolutePosition, m_autoPilotTarget) <= 1.5)
            {
                if (m_sitAtAutoTarget)
                {
                    SceneObjectPart part = m_scene.GetSceneObjectPart(m_requestedSitTargetUUID);
                    if (part != null)
                    {
                        m_autoPilotTarget = Vector3.Zero;
                        m_autopilotMoving = false;
                        SendSitResponse(ControllingClient, m_requestedSitTargetUUID, Vector3.Zero, Quaternion.Identity);
                        m_requestedSitTargetUUID = UUID.Zero;
                    }
                    m_requestedSitTargetUUID = UUID.Zero;
                }
            }
        }

        /// <summary>
        /// Perform the logic necessary to stand the avatar up.  This method also executes
        /// the stand animation.
        /// </summary>
        public void StandUp()
        {
            SitGround = false;

            if (m_parentID != UUID.Zero)
            {
                SceneObjectPart part = m_scene.GetSceneObjectPart(m_parentID);
                if (part != null)
                {
                    //Block movement of vehicles for a bit until after the changed event has fired
                    if(part.PhysActor != null)
                        part.PhysActor.Selected = true;
                    TaskInventoryDictionary taskIDict = part.TaskInventory;
                    if (taskIDict != null)
                    {
                        lock (taskIDict)
                        {
                            foreach (UUID taskID in taskIDict.Keys)
                            {
                                UnRegisterControlEventsToScript(LocalId, taskID);
                                taskIDict[taskID].PermsMask &= ~(
                                    (int)ScriptPermission.ControlCamera |
                                    (int)ScriptPermission.TakeControls);
                            }
                        }

                    }
                    // Reset sit target.
                    if (part.GetAvatarOnSitTarget().Contains(UUID))
                        part.RemoveAvatarOnSitTarget(UUID);

                    m_parentPosition = part.GetWorldPosition();
                    Vector3 MovePos = new Vector3();
                    MovePos.X = 1; //TODO: Make this configurable
                    MovePos *= Rotation;
                    m_parentPosition += MovePos;
                    ControllingClient.SendClearFollowCamProperties(part.ParentUUID);
                }

                if (m_physicsActor == null)
                {
                    AddToPhysicalScene(false, false);
                }

                m_pos += m_parentPosition + new Vector3(0.0f, 0.0f, 2.0f*m_sitAvatarHeight);
                m_parentPosition = Vector3.Zero;

                m_parentID = UUID.Zero;
                SendAvatarDataToAllAgents();
                m_requestedSitTargetUUID = UUID.Zero;
                if ((m_physicsActor != null) && (m_avHeight > 0))
                {
                    SetHeight(m_avHeight);
                }
            }

            Animator.TrySetMovementAnimation("STAND");
        }

        private SceneObjectPart FindNextAvailableSitTarget(UUID targetID)
        {
            SceneObjectPart targetPart = m_scene.GetSceneObjectPart(targetID);
            if (targetPart == null)
                return null;

            // If the primitive the player clicked on has a sit target and that sit target is not full, that sit target is used.
            // If the primitive the player clicked on has no sit target, and one or more other linked objects have sit targets that are not full, the sit target of the object with the lowest link number will be used.

            // Get our own copy of the part array, and sort into the order we want to test
            SceneObjectPart[] partArray = targetPart.ParentGroup.Parts;
            Array.Sort(partArray, delegate(SceneObjectPart p1, SceneObjectPart p2)
                       {
                           // we want the originally selected part first, then the rest in link order -- so make the selected part link num (-1)
                           int linkNum1 = p1==targetPart ? -1 : p1.LinkNum;
                           int linkNum2 = p2==targetPart ? -1 : p2.LinkNum;
                           return linkNum1 - linkNum2;
                       }
                );

            //look for prims with explicit sit targets that are available
            foreach (SceneObjectPart part in partArray)
            {
                // Is a sit target available?
                Vector3 avSitOffSet = part.SitTargetPosition;
                Quaternion avSitOrientation = part.SitTargetOrientation;

                bool SitTargetisSet =
                    (!(avSitOffSet.X == 0f && avSitOffSet.Y == 0f && avSitOffSet.Z == 0f && avSitOrientation.W == 1f &&
                       avSitOrientation.X == 0f && avSitOrientation.Y == 0f && avSitOrientation.Z == 0f));

                if (SitTargetisSet)
                {
                    //switch the target to this prim
                    return part;
                }
            }

            // no explicit sit target found - use original target
            return targetPart;
        }

        public void CrossSittingAgent(IClientAPI remoteClient, UUID targetID)
        {
            if (String.IsNullOrEmpty(m_nextSitAnimation))
            {
                m_nextSitAnimation = "SIT";
            }

            SceneObjectPart part = FindNextAvailableSitTarget(targetID);

            if (!String.IsNullOrEmpty(part.SitAnimation))
            {
                m_nextSitAnimation = part.SitAnimation;
            }
            m_requestedSitOffset = Vector3.Zero;
            m_requestedSitTargetUUID = targetID;

            Vector3 sitTargetPos = part.SitTargetPosition;
            Quaternion sitTargetOrient = part.SitTargetOrientation;

            m_pos = new Vector3(sitTargetPos.X, sitTargetPos.Y, sitTargetPos.Z);
            m_pos += SIT_TARGET_ADJUSTMENT;
            m_bodyRot = sitTargetOrient;
            m_parentPosition = part.AbsolutePosition;
            m_parentID = m_requestedSitTargetUUID;

            Velocity = Vector3.Zero;
            RemoveFromPhysicalScene();

            SendAvatarDataToAllAgents();
            Animator.TrySetMovementAnimation(m_nextSitAnimation);
        }

        private void SendSitResponse(IClientAPI remoteClient, UUID targetID, Vector3 offset, Quaternion pSitOrientation)
        {
            bool autopilot = true;
            Vector3 pos = new Vector3();
            Quaternion sitOrientation = pSitOrientation;
            Vector3 cameraEyeOffset = Vector3.Zero;
            Vector3 cameraAtOffset = Vector3.Zero;
            bool forceMouselook = false;

            SceneObjectPart part =  FindNextAvailableSitTarget(targetID);
            m_requestedSitTargetUUID = part.UUID;
            if (part != null)
            {
                // UNTODO: determine position to sit at based on scene geometry; don't trust offset from client
                // see http://wiki.secondlife.com/wiki/User:Andrew_Linden/Office_Hours/2007_11_06 for details on how LL does it

                // Is a sit target available?
                Vector3 avSitOffSet = part.SitTargetPosition;
                Quaternion avSitOrientation = part.SitTargetOrientation;
                bool SitTargetUnOccupied = true;
                bool UseSitTarget = false;

                bool SitTargetisSet =
                    (!(avSitOffSet.X == 0f && avSitOffSet.Y == 0f && avSitOffSet.Z == 0f &&
                       (
                           avSitOrientation.X == 0f && avSitOrientation.Y == 0f && avSitOrientation.Z == 0f && avSitOrientation.W == 1f // Valid Zero Rotation quaternion
                           || avSitOrientation.X == 0f && avSitOrientation.Y == 0f && avSitOrientation.Z == 1f && avSitOrientation.W == 0f // W-Z Mapping was invalid at one point
                           || avSitOrientation.X == 0f && avSitOrientation.Y == 0f && avSitOrientation.Z == 0f && avSitOrientation.W == 0f // Invalid Quaternion
                       )
                       ));

                m_requestedSitTargetUUID = part.UUID;
                part.SetAvatarOnSitTarget(UUID);

                if (SitTargetisSet && SitTargetUnOccupied)
                {
                    m_requestedSitTargetUUID = part.UUID;
                    offset = new Vector3(avSitOffSet.X, avSitOffSet.Y, avSitOffSet.Z);
                    sitOrientation = avSitOrientation;
                    autopilot = false;
                    UseSitTarget = true;
                }

                pos = part.AbsolutePosition;// +offset;
                if (m_physicsActor != null)
                {
                    // If we're not using the client autopilot, we're immediately warping the avatar to the location
                    // We can remove the physicsActor until they stand up.
                    m_sitAvatarHeight = m_physicsActor.Size.Z;

                    if (autopilot)
                    {
                        Vector3 targetpos = new Vector3(m_pos.X - part.AbsolutePosition.X - (part.Scale.X / 2),
                            m_pos.Y - part.AbsolutePosition.Y - (part.Scale.Y / 2),
                            m_pos.Z - part.AbsolutePosition.Z - (part.Scale.Z / 2));
                        if (targetpos.Length() < 4.5)
                        {
                            autopilot = false;
                            Velocity = Vector3.Zero;
                            RemoveFromPhysicalScene();
                            Vector3 Position = part.AbsolutePosition;
                            Vector3 MovePos = Vector3.Zero;
                            if (part.GetPrimType() == PrimType.BOX ||
                                part.GetPrimType() == PrimType.CYLINDER ||
                                part.GetPrimType() == PrimType.TORUS ||
                                part.GetPrimType() == PrimType.TUBE ||
                                part.GetPrimType() == PrimType.RING ||
                                part.GetPrimType() == PrimType.PRISM ||
                                part.GetPrimType() == PrimType.SCULPT)
                            {
                                Position.Z += part.Scale.Z / 2f;
                                Position.Z += m_appearance.AvatarHeight / 2;
                                Position.Z -= (float)(SIT_TARGET_ADJUSTMENT.Z / 1.5);//m_appearance.AvatarHeight / 15;

                                MovePos.X = (part.Scale.X / 2) + .1f;
                                MovePos *= Rotation;
                            }
                            else if (part.GetPrimType() == PrimType.SPHERE)
                            {
                                Position.Z += part.Scale.Z / 2f;
                                Position.Z += m_appearance.AvatarHeight / 2;
                                Position.Z -= (float)(SIT_TARGET_ADJUSTMENT.Z / 1.5);//m_appearance.AvatarHeight / 15;

                                MovePos.X = (float)(part.Scale.X / 2.5);
                                MovePos *= Rotation;
                            }
                            Position += MovePos;
                            AbsolutePosition = Position;
                        }
                    }
                    else
                    {
                        RemoveFromPhysicalScene();
                    }
                }

                cameraAtOffset = part.CameraAtOffset;
                cameraEyeOffset = part.CameraEyeOffset;
                forceMouselook = part.ForceMouselook;

                ControllingClient.SendSitResponse(targetID, offset, sitOrientation, autopilot, cameraAtOffset, cameraEyeOffset, forceMouselook);
                // This calls HandleAgentSit twice, once from here, and the client calls
                // HandleAgentSit itself after it gets to the location
                // It doesn't get to the location until we've moved them there though
                // which happens in HandleAgentSit :P
                m_autopilotMoving = autopilot;
                m_autoPilotTarget = pos;
                m_sitAtAutoTarget = autopilot;
                if (!autopilot)
                    HandleAgentSit(remoteClient, UUID, UseSitTarget);
            }
            else
                m_log.Warn("Sit requested on unknown object: " + targetID);
        }

        public void HandleAgentRequestSit(IClientAPI remoteClient, UUID agentID, UUID targetID, Vector3 offset)
        {
            if (m_parentID != UUID.Zero)
                StandUp();

            m_nextSitAnimation = "SIT";
            
            //SceneObjectPart part = m_scene.GetSceneObjectPart(targetID);
            SceneObjectPart part = FindNextAvailableSitTarget(targetID);

            if (part != null)
            {
                if (!String.IsNullOrEmpty(part.SitAnimation))
                {
                    m_nextSitAnimation = part.SitAnimation;
                }
                m_requestedSitOffset = offset;
                m_requestedSitTargetUUID = targetID;
                
                //m_log.DebugFormat("[SIT]: Client requested Sit Position: {0}", offset);
                
                //if (m_scene.PhysicsScene.SupportsRayCast())
                //{
                    //m_scene.PhysicsScene.RaycastWorld(Vector3.Zero,Vector3.Zero, 0.01f, SitRayCastAvatarPositionResponse);
                    //SitRayCastAvatarPosition(part);
                    //return;
                //}
                SendSitResponse(remoteClient, targetID, offset, Quaternion.Identity);
            }
            else
            {
                m_log.Warn("Sit requested on unknown object: " + targetID.ToString());
            }
        }
        
        public void HandleAgentRequestSit(IClientAPI remoteClient, UUID agentID, UUID targetID, Vector3 offset, string sitAnimation)
        {
            if (m_parentID != UUID.Zero)
                StandUp();

            if (!String.IsNullOrEmpty(sitAnimation))
            {
                m_nextSitAnimation = sitAnimation;
            }
            else
            {
                m_nextSitAnimation = "SIT";
            }

            SceneObjectPart part =  FindNextAvailableSitTarget(targetID);
            if (part != null)
            {
                m_requestedSitOffset = offset;
                m_requestedSitTargetUUID = targetID;

                m_log.DebugFormat("[SIT]: Client requested Sit Position: {0}", offset);

                //if (m_scene.PhysicsScene.SupportsRayCast())
                //{
                    //SitRayCastAvatarPosition(part);
                    //return;
                //}
                SendSitResponse(remoteClient, targetID, offset, Quaternion.Identity);
            }
            else
                m_log.Warn("Sit requested on unknown object: " + targetID);
        }

        public void HandleAgentSit(IClientAPI remoteClient, UUID agentID)
        {
            if (!String.IsNullOrEmpty(m_nextSitAnimation))
            {
                HandleAgentSit(remoteClient, agentID, m_nextSitAnimation, false);
            }
            else
            {
                HandleAgentSit(remoteClient, agentID, "SIT", false);
            }
        }

        public void HandleAgentSit(IClientAPI remoteClient, UUID agentID, bool UseSitTarget)
        {
            if (!String.IsNullOrEmpty(m_nextSitAnimation))
            {
                HandleAgentSit(remoteClient, agentID, m_nextSitAnimation, UseSitTarget);
            }
            else
            {
                HandleAgentSit(remoteClient, agentID, "SIT", UseSitTarget);
            }
        }
        
        public void HandleAgentSit(IClientAPI remoteClient, UUID agentID, string sitAnimation, bool UseSitTarget)
        {
            SceneObjectPart part = m_scene.GetSceneObjectPart(m_requestedSitTargetUUID);
            if (part != null)
            {
                if (m_sitAtAutoTarget || !m_autopilotMoving)
                {
                    if (UseSitTarget)
                    {
                        Vector3 sitTargetPos = part.SitTargetPosition;
                        Quaternion sitTargetOrient = part.SitTargetOrientation;

                        m_pos = new Vector3(sitTargetPos.X, sitTargetPos.Y, sitTargetPos.Z);
                        m_pos += SIT_TARGET_ADJUSTMENT;
                        m_bodyRot = sitTargetOrient;
                        m_parentPosition = part.AbsolutePosition;
                    }
                    else
                    {
                        m_pos -= part.AbsolutePosition;
                        m_parentPosition = part.AbsolutePosition;
                    }
                }
                m_parentID = m_requestedSitTargetUUID;

                Velocity = Vector3.Zero;
                try
                {
                    RemoveFromPhysicalScene();
                }
                catch (Exception ex)
                {
                    m_log.Warn(ex);
                }

                Animator.TrySetMovementAnimation(sitAnimation);
                SendAvatarDataToAllAgents();
                // This may seem stupid, but Our Full updates don't send avatar rotation :P
                // So we're also sending a terse update (which has avatar rotation)
                // [Update] We do now.
                //SendTerseUpdateToAllClients();
            }
        }

        /// <summary>
        /// Event handler for the 'Always run' setting on the client
        /// Tells the physics plugin to increase speed of movement.
        /// </summary>
        public void HandleSetAlwaysRun(IClientAPI remoteClient, bool pSetAlwaysRun)
        {
            m_setAlwaysRun = pSetAlwaysRun;
            if (PhysicsActor != null)
            {
                PhysicsActor.SetAlwaysRun = pSetAlwaysRun;
            }
        }

        public void HandleStartAnim(IClientAPI remoteClient, UUID animID)
        {
            if (animID == Animations.BUSY)
                m_isBusy = true;
            Animator.AddAnimation(animID, UUID.Zero);
        }

        public void HandleStopAnim(IClientAPI remoteClient, UUID animID)
        {
            if (animID == Animations.BUSY)
                m_isBusy = false;
            Animator.RemoveAnimation(animID);
        }

        public Vector3 PreJumpForce = Vector3.Zero;

        /// <summary>
        /// Rotate the avatar to the given rotation and apply a movement in the given relative vector
        /// </summary>
        /// <param name="vec">The vector in which to move.  This is relative to the rotation argument</param>
        /// <param name="rotation">The direction in which this avatar should now face.</param>
        public void AddNewMovement(Vector3 vec, Quaternion rotation)
        {
            if (IsChildAgent)
            {
                // WHAT??? we can't make them a root agent though... what if they shouldn't be here?
                //  Or even worse, what if they are spoofing the client???
                m_log.Info("[SCENEPRESENCE]: AddNewMovement() called on child agent for " + Name + "! Possible attempt to force a fake agent into a sim!");
                return;
            }

            m_perfMonMS = Util.EnvironmentTickCount();

            PhysicsActor actor = m_physicsActor;
            if (actor != null)
            {
                Vector3 direc = (rotation == Quaternion.Identity ? vec : (vec * rotation));
                Rotation = rotation;
                direc.Normalize();
                direc *= 6 * m_speedModifier;



                // scale it up acording to situation

                if (actor.Flying)
                {
                    direc *= 4.0f;
                    //bool controlland = (((m_AgentControlFlags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_UP_NEG) != 0) || ((m_AgentControlFlags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_NEG) != 0));
                    //bool colliding = (m_physicsActor.IsColliding==true);
                    //if (controlland)
                    //    m_log.Info("[AGENT]: landCommand");
                    //if (colliding)
                    //    m_log.Info("[AGENT]: colliding");
                    //if (m_physicsActor.Flying && colliding && controlland)
                    //{
                    //    StopFlying();
                    //    m_log.Info("[AGENT]: Stop FLying");
                    //}
                }
                else if (!actor.Flying && actor.IsColliding)
                {
                    if (direc.Z > 2.0f)
                    {
                        if (Velocity.Z <= .25 && Velocity.Z >= -0.25)
                        {
                            if (direc.Z < 2.5f)
                                direc.Z = 2.5f;
                            if (m_scene.m_usePreJump && !IsJumping)
                            {
                                //AllowMovement = false;
                                IsJumping = true;
                                PreJumpForce = direc;
                                Animator.TrySetMovementAnimation("PREJUMP");
                                //Leave this here! Otherwise jump will sometimes not occur...
                                return;
                            }
                            else if (PreJumpForce.Equals(Vector3.Zero))
                            {
                                direc.X *= 2;
                                direc.Y *= 2;
                                if (direc.X == 0 && direc.Y == 0)
                                    direc.Z *= 2f;
                                else
                                    direc.Z *= 3f;

                                if (!IsJumping)
                                    Animator.TrySetMovementAnimation("JUMP");
                            }
                        }
                        else //Jumping while moving vertically... stop it
                            return;
                    }
                }


                // UNTODO: Add the force instead of only setting it to support multiple forces per frame?
                // It fires multiple time and screws things up...
                if (!m_overrideUserInput)
                {
                    //This is where you start to decay the velocity
                    //direc *= 0.95f;
                    //More decay on the Z, otherwise flying up and down is a bit hard

                    //                     this does not acumulate and is just a constant
                    direc.Z = direc.Z * 0.5f;
                    
                    //It'll stop the physics engine from decaying, which makes it look bad
                    //if (this.m_newStyleMovement && direc != Vector3.Zero)//  let avas be stopped !!
                    if (direc == Vector3.Zero)
                        PhysicsActor.Velocity = Vector3.Zero;
                    //else
                        PhysicsActor.SetMovementForce(direc);
                }
                IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
                if (reporter != null)
                    reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(m_perfMonMS));
            }
        }

        #endregion

        #region Overridden Methods

        public override void Update()
        {
            //Util.FireAndForget(SendPrimUpdates);
            //We 'could' do this async... but we don't do anything else here, so why?
            m_sceneViewer.SendPrimUpdates();
            //if (!IsChildAgent)
            //{
            //    if (m_parentID != UUID.Zero)
            //    {
            //        SceneObjectPart part = Scene.GetSceneObjectPart(m_parentID);
            //        if (part != null)
            //        {
            //            if ((m_AgentControlFlags & AgentManager.ControlFlags.AGENT_CONTROL_MOUSELOOK) == AgentManager.ControlFlags.AGENT_CONTROL_MOUSELOOK)
            //                part.SetPhysActorCameraPos(Lookat);
            //            else
            //                part.SetPhysActorCameraPos(Vector3.Zero);
            //        }
            //    }
            //}
            if (m_enqueueSendChildAgentUpdate &&
                m_enqueueSendChildAgentUpdateTime != new DateTime() &&
                DateTime.Now > m_enqueueSendChildAgentUpdateTime)
            {
                //Send the child agent data update
                INeighborService neighborService = m_scene.RequestModuleInterface<INeighborService>();
                if (neighborService != null)
                {
                    AgentData data = new AgentData();
                    this.CopyTo(data);
                    neighborService.SendChildAgentUpdate(data, m_scene.RegionInfo.RegionID);
                }
                //Reset it now
                m_enqueueSendChildAgentUpdateTime = new DateTime();
                m_enqueueSendChildAgentUpdate = false;
            }
        }

        #endregion

        #region Update Client(s)

        /// <summary>
        /// Sends a location update to the client connected to this scenePresence
        /// </summary>
        /// <param name="remoteClient"></param>
        public void SendTerseUpdateToClient(IClientAPI remoteClient)
        {
            // If the client is inactive, it's getting its updates from another
            // server.
            if (remoteClient.IsActive)
            {
                //m_log.DebugFormat("[SCENEPRESENCE]: TerseUpdate: Pos={0} Rot={1} Vel={2}", m_pos, m_bodyRot, m_velocity);

                remoteClient.SendPrimUpdate(
                    this,
                    PrimUpdateFlags.Position | PrimUpdateFlags.Rotation | PrimUpdateFlags.Velocity
                    | PrimUpdateFlags.Acceleration | PrimUpdateFlags.AngularVelocity, 0);

                IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
                if (reporter != null)
                {
                    reporter.AddAgentUpdates(1);
                }
            }
        }

        /// <summary>
        /// Send a location/velocity/accelleration update to all agents in scene
        /// </summary>
        public void SendTerseUpdateToAllClients()
        {
            m_perfMonMS = Util.EnvironmentTickCount();
            
            m_scene.ForEachClient(SendTerseUpdateToClient);

            IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
            if (reporter != null)
                reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(m_perfMonMS));
        }

        public void SendCoarseLocations(List<Vector3> coarseLocations, List<UUID> avatarUUIDs)
        {
            SendCourseLocationsMethod d = m_sendCourseLocationsMethod;
            if (d != null)
            {
                d.Invoke(m_scene.RegionInfo.RegionID, this, coarseLocations, avatarUUIDs);
            }
        }

        public void SetSendCourseLocationMethod(SendCourseLocationsMethod d)
        {
            if (d != null)
                m_sendCourseLocationsMethod = d;
        }

        public void SendCoarseLocationsDefault(UUID sceneId, ScenePresence p, List<Vector3> coarseLocations, List<UUID> avatarUUIDs)
        {
            m_perfMonMS = Util.EnvironmentTickCount();
            m_controllingClient.SendCoarseLocationUpdate(avatarUUIDs, coarseLocations);
            IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
            if (reporter != null)
                reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(m_perfMonMS));
        }

        /// <summary>
        /// Send this agent's avatar data to all other root and child agents in the scene
        /// This agent must be root. This avatar will receive its own update. 
        /// </summary>
        public void SendAvatarDataToAllAgents()
        {
            // only send update from root agents to other clients; children are only "listening posts"
            if (IsChildAgent)
            {
                m_log.Warn("[SCENEPRESENCE] attempt to send avatar data from a child agent");
                return;
            }

            m_perfMonMS = Util.EnvironmentTickCount();

            int count = 0;
            m_scene.ForEachScenePresence(delegate(ScenePresence scenePresence)
                                         {
                                             SendAvatarDataToAgent(scenePresence);
                                             count++;
                                         });

            IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
            if (reporter != null)
            {
                reporter.AddAgentUpdates(count);
                reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(m_perfMonMS));
            }
        }

        /// <summary>
        /// Send avatar data for all other root agents to this agent, this agent
        /// can be either a child or root
        /// </summary>
        public void SendOtherAgentsAvatarDataToMe()
        {
            m_perfMonMS = Util.EnvironmentTickCount();
            int count = 0;
            m_scene.ForEachScenePresence(delegate(ScenePresence scenePresence)
                                         {
                                             // only send information about root agents
                                             if (scenePresence.IsChildAgent)
                                                 return;

                                             // only send information about other root agents
                                             if (scenePresence.UUID == UUID)
                                                 return;

                                             scenePresence.SendAvatarDataToAgent(this);
                                             count++;
                                         });

            IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
            if (reporter != null)
            {
                reporter.AddAgentUpdates(count);
                reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(m_perfMonMS));
            }
        }

        /// <summary>
        /// Send avatar data to an agent.
        /// </summary>
        /// <param name="avatar"></param>
        private void SendAvatarDataToAgent(ScenePresence avatar)
        {
            //m_log.WarnFormat("[SP] Send avatar data from {0} to {1}",m_uuid,avatar.ControllingClient.AgentId);
            avatar.ControllingClient.SendAvatarDataImmediate(this);
            Animator.SendAnimPackToClient(avatar.ControllingClient);
        }

        /// <summary>
        /// Send this agent's appearance to all other root and child agents in the scene
        /// This agent must be root.
        /// </summary>
        public void SendAppearanceToAllOtherAgents()
        {
            // only send update from root agents to other clients; children are only "listening posts"
            if (IsChildAgent)
            {
                m_log.Warn("[SCENEPRESENCE] attempt to send avatar data from a child agent");
                return;
            }

            m_perfMonMS = Util.EnvironmentTickCount();

            int count = 0;
            m_scene.ForEachScenePresence(delegate(ScenePresence scenePresence)
                                         {
                                             if (scenePresence.UUID == UUID)
                                                 return;

                                             SendAppearanceToAgent(scenePresence);
                                             count++;
                                         });

            IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
            if (reporter != null)
            {
                reporter.AddAgentUpdates(count);
                reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(m_perfMonMS));
            }
        }

        /// <summary>
        /// Send appearance from all other root agents to this agent. this agent
        /// can be either root or child
        /// </summary>
        public void SendOtherAgentsAppearanceToMe()
        {
            m_perfMonMS = Util.EnvironmentTickCount();

            int count = 0;
            m_scene.ForEachScenePresence(delegate(ScenePresence scenePresence)
            {
                // only send information about root agents
                if (scenePresence.IsChildAgent)
                    return;

                // only send information about other root agents
                if (scenePresence.UUID == UUID)
                    return;

                scenePresence.SendAppearanceToAgent(this);
                count++;
            });

            IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
            if (reporter != null)
            {
                reporter.AddAgentUpdates(count);
                reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(m_perfMonMS));
            }
        }

        /// <summary>
        /// Send appearance data to an agent.
        /// </summary>
        /// <param name="avatar"></param>
        public void SendAppearanceToAgent(ScenePresence avatar)
        {
            avatar.ControllingClient.SendAppearance(
                m_appearance.Owner, m_appearance.VisualParams, m_appearance.Texture.GetBytes());
        }

        // Because appearance setting is in a module, we actually need
        // to give it access to our appearance directly, otherwise we
        // get a synchronization issue.
        public AvatarAppearance Appearance
        {
            get { return m_appearance; }
            set { m_appearance = value; }
        }

        #endregion

        #region Significant Movement Method

        /// <summary>
        /// This checks for a significant movement and sends a courselocationchange update
        /// </summary>
        protected void CheckForSignificantMovement()
        {
            // Movement updates for agents in neighboring regions are sent directly to clients.
            // This value only affects how often agent positions are sent to neighbor regions
            // for things such as distance-based update prioritization

            if (Util.GetDistanceTo(AbsolutePosition, posLastSignificantMove) > SIGNIFICANT_MOVEMENT)
            {
                posLastSignificantMove = AbsolutePosition;
                m_scene.EventManager.TriggerSignificantClientMovement(m_controllingClient);
            }

            // Minimum Draw distance is 64 meters, the Radius of the draw distance sphere is 32m
            if (Util.GetDistanceTo(AbsolutePosition, m_lastChildAgentUpdatePosition) >= m_sceneViewer.Prioritizer.ChildReprioritizationDistance ||
                Util.GetDistanceTo(CameraPosition, m_lastChildAgentUpdateCamPosition) >= m_sceneViewer.Prioritizer.ChildReprioritizationDistance)
            {
                m_lastChildAgentUpdatePosition = AbsolutePosition;
                m_lastChildAgentUpdateCamPosition = CameraPosition;

                AgentPosition agentpos = new AgentPosition();
                agentpos.AgentID = UUID;
                agentpos.AtAxis = CameraAtAxis;
                agentpos.Center = m_lastChildAgentUpdateCamPosition;
                agentpos.ChangedGrid = false;
                agentpos.CircuitCode = 0;
                agentpos.Far = DrawDistance;
                agentpos.LeftAxis = CameraLeftAxis;
                agentpos.Position = m_lastChildAgentUpdatePosition;
                agentpos.RegionHandle = Scene.RegionInfo.RegionHandle;
                agentpos.SessionID = UUID.Zero;
                agentpos.Size = PhysicsActor != null ? PhysicsActor.Size : new Vector3(0, 0, m_avHeight);
                agentpos.Throttles = new byte[0];
                agentpos.UpAxis = CameraUpAxis;
                agentpos.Velocity = Velocity;

                ISyncMessagePosterService syncPoster = Scene.RequestModuleInterface<ISyncMessagePosterService>();
                if (syncPoster != null)
                    syncPoster.Post(SyncMessageHelper.SendChildAgentUpdate(agentpos, m_scene.RegionInfo.RegionHandle));
            }

            //Moving these into the terse update check, as they don't need to be checked/sent unless the client has moved.
            // followed suggestion from mic bowman. reversed the two lines below.
            if (((m_parentID == UUID.Zero && m_physicsActor != null) ||
                m_parentID != UUID.Zero) &&
                (m_physicsActor != null &&
                m_physicsActor.Position.IsFinite())) // Check that we have a physics actor or we're sitting on something
                CheckForBorderCrossing();

            //Moving collision sound ID inside this loop so that we don't trigger it too much
            if (CollisionSoundID != UUID.Zero)
            {
                ISoundModule module = Scene.RequestModuleInterface<ISoundModule>();
                module.TriggerSound(CollisionSoundID, UUID, UUID, UUID.Zero, 1, AbsolutePosition, Scene.RegionInfo.RegionHandle, 100);
                CollisionSoundID = UUID.Zero;
            }
        }

        #endregion

        #region Border Crossing Methods

        /// <summary>
        /// Checks to see if the avatar is in range of a border and calls CrossToNewRegion
        /// </summary>
        protected void CheckForBorderCrossing()
        {
            if (IsChildAgent)
                return;
            //Don't check if the avatar is sitting on something. Crossing should be called
            // directly by the SOG if the object needs to cross.
            if (m_parentID != UUID.Zero)
                return;

            Vector3 pos2 = AbsolutePosition;
            Vector3 vel = Velocity;
            int[] fix = new int[2];

            float timeStep = 0.1f;
            pos2.X = pos2.X + (vel.X*timeStep);
            pos2.Y = pos2.Y + (vel.Y*timeStep);
            pos2.Z = pos2.Z + (vel.Z*timeStep);

            if (!IsInTransit)
            {
                if (pos2.X < 0f || pos2.Y < 0f ||
                    pos2.X > Scene.RegionInfo.RegionSizeX || pos2.Y > Scene.RegionInfo.RegionSizeY)
                {
                    //If we are headed out of the region, make sure we have a region there
                    INeighborService neighborService = Scene.RequestModuleInterface<INeighborService>();
                    if (neighborService != null)
                    {
                        List<GridRegion> neighbors = neighborService.GetNeighbors(Scene.RegionInfo);

                        int RegionCrossX = Scene.RegionInfo.RegionLocX;
                        int RegionCrossY = Scene.RegionInfo.RegionLocY;

                        if (pos2.X < 0f) RegionCrossX -= Constants.RegionSize;
                        if (pos2.Y < 0f) RegionCrossY -= Constants.RegionSize;
                        if (pos2.X > Scene.RegionInfo.RegionSizeX) RegionCrossX += (int)Scene.RegionInfo.RegionSizeX;
                        if (pos2.Y > Scene.RegionInfo.RegionSizeY) RegionCrossY += (int)Scene.RegionInfo.RegionSizeY;
                        GridRegion neighborRegion = null;

                        foreach (GridRegion region in neighbors)
                        {
                            if (region.RegionLocX == RegionCrossX &&
                                region.RegionLocY == RegionCrossY)
                            {
                                neighborRegion = region;
                                break;
                            }
                        }

                        if (neighborRegion != null)
                        {
                            InTransit();
                            bool isFlying = false;
                            if (m_physicsActor != null)
                                isFlying = m_physicsActor.Flying;

                            IEntityTransferModule transferModule = Scene.RequestModuleInterface<IEntityTransferModule>();
                            if (transferModule != null)
                                transferModule.Cross(this, isFlying, neighborRegion);
                            else
                                m_log.DebugFormat("[ScenePresence]: Unable to cross agent to neighbouring region, because there is no AgentTransferModule");
                        }
                        else
                            m_log.Debug("[ScenePresence]: Could not find region for " + Name + " to cross into @ {" + RegionCrossX / 256 + ", " + RegionCrossY / 256 + "}");
                    }
                }
            }
            else
            {
                //Crossings are much nastier if this code is enabled
                /*RemoveFromPhysicalScene();
                // This constant has been inferred from experimentation
                // I'm not sure what this value should be, so I tried a few values.
                timeStep = 0.04f;
                pos2 = AbsolutePosition;
                pos2.X = pos2.X + (vel.X * timeStep);
                pos2.Y = pos2.Y + (vel.Y * timeStep);
                pos2.Z = pos2.Z + (vel.Z * timeStep);
                m_pos = pos2;*/
            }
        }

        public void InTransit()
        {
            m_inTransit = true;

            if ((m_physicsActor != null) && m_physicsActor.Flying)
                m_AgentControlFlags |= AgentManager.ControlFlags.AGENT_CONTROL_FLY;
            else if ((m_AgentControlFlags & AgentManager.ControlFlags.AGENT_CONTROL_FLY) != 0)
                m_AgentControlFlags &= ~AgentManager.ControlFlags.AGENT_CONTROL_FLY;
        }

        public void NotInTransit()
        {
            m_inTransit = false;
        }

        private void Reset()
        {
            //Reset the parcel UUID for the user
            currentParcelUUID = UUID.Zero;
            // Put the child agent back at the center
            AbsolutePosition
                = new Vector3(Scene.RegionInfo.RegionSizeX * 0.5f, Scene.RegionInfo.RegionSizeY * 0.5f, 70);
            if(Animator != null)
                Animator.ResetAnimations();
            m_parentID = UUID.Zero;
            m_parentPosition = Vector3.Zero;
            ControllingClient.Reset();
        }

        #endregion

        #region Child Agent Updates

        public void ChildAgentDataUpdate(AgentData cAgentData)
        {
            //m_log.Debug("   >>> ChildAgentDataUpdate <<< " + Scene.RegionInfo.RegionName);
            if (!IsChildAgent)
                return;

            CopyFrom(cAgentData);
        }

        /// <summary>
        /// This updates important decision making data about a child agent
        /// The main purpose is to figure out what objects to send to a child agent that's in a neighboring region
        /// </summary>
        public void ChildAgentDataUpdate(AgentPosition cAgentData, int tRegionX, int tRegionY, int rRegionX, int rRegionY)
        {
            if (!IsChildAgent)
                return;

            //m_log.Debug("   >>> ChildAgentPositionUpdate <<< " + rRegionX + "-" + rRegionY);
            int shiftx = rRegionX - tRegionX;
            int shifty = rRegionY - tRegionY;

            Vector3 offset = new Vector3(shiftx, shifty, 0f);

            DrawDistance = cAgentData.Far;
            if (cAgentData.Position != new Vector3(-1f, -1f, -1f)) // UGH!!
                m_pos = cAgentData.Position + offset;

            m_CameraCenter = cAgentData.Center + offset;

            m_avHeight = cAgentData.Size.Z;

            if ((cAgentData.Throttles != null) && cAgentData.Throttles.Length > 0)
                ControllingClient.SetChildAgentThrottle(cAgentData.Throttles);

            m_scene.EventManager.TriggerSignificantClientMovement(m_controllingClient);

            m_sceneViewer.Reprioritize();
            //m_velocity = cAgentData.Velocity;
        }

        public void CopyTo(AgentData cAgent)
        {
            cAgent.AgentID = UUID;
            cAgent.RegionID = Scene.RegionInfo.RegionID;

            cAgent.Position = AbsolutePosition;
            cAgent.Velocity = Velocity;
            cAgent.Center = m_CameraCenter;
            // Don't copy the size; it is inferred from appearance parameters
            //cAgent.Size = new Vector3(0, 0, m_avHeight);
            cAgent.AtAxis = m_CameraAtAxis;
            cAgent.LeftAxis = m_CameraLeftAxis;
            cAgent.UpAxis = m_CameraUpAxis;

            cAgent.Far = DrawDistance;

            // Throttles 
            float multiplier = 1;
            int innacurateNeighbors = m_scene.RequestModuleInterface<INeighborService>().GetNeighbors(m_scene.RegionInfo).Count;
            if (innacurateNeighbors != 0)
            {
                multiplier = 1f / innacurateNeighbors;
            }
            if (multiplier <= 0.25f)
            {
                multiplier = 0.25f;
            }
            //m_log.Info("[NeighborThrottle]: " + m_scene.GetInaccurateNeighborCount().ToString() + " - m: " + multiplier.ToString());
            cAgent.Throttles = ControllingClient.GetThrottlesPacked(multiplier);

            cAgent.HeadRotation = m_headrotation;
            cAgent.BodyRotation = m_bodyRot;
            cAgent.ControlFlags = (uint)m_AgentControlFlags;

            //This is checked by the other sim, so we don't have to validate it at all
            //if (m_scene.Permissions.IsGod(new UUID(cAgent.AgentID)))
            cAgent.GodLevel = (byte)m_godLevel;
            //else 
            //    cAgent.GodLevel = (byte) 0;

            cAgent.Speed = SpeedModifier;
            cAgent.DrawDistance = DrawDistance;
            cAgent.AlwaysRun = m_setAlwaysRun;
            cAgent.SentInitialWearables = m_InitialHasWearablesBeenSent;

            cAgent.Appearance = new AvatarAppearance(m_appearance);

            lock (scriptedcontrols)
            {
                ControllerData[] controls = new ControllerData[scriptedcontrols.Count];
                int i = 0;

                foreach (ScriptControllers c in scriptedcontrols.Values)
                {
                    controls[i++] = new ControllerData(c.itemID, (uint)c.ignoreControls, (uint)c.eventControls);
                }
                cAgent.Controllers = controls;
            }

            // Animations
            if (Animator != null)
                cAgent.Anims = Animator.Animations.ToArray();
        }

        public void CopyFrom(AgentData cAgent)
        {
            try
            {
                m_pos = cAgent.Position;
                Velocity = cAgent.Velocity;
                m_CameraCenter = cAgent.Center;
                //m_avHeight = cAgent.Size.Z;
                m_CameraAtAxis = cAgent.AtAxis;
                m_CameraLeftAxis = cAgent.LeftAxis;
                m_CameraUpAxis = cAgent.UpAxis;

                DrawDistance = cAgent.Far;

                if ((cAgent.Throttles != null) && cAgent.Throttles.Length > 0)
                    ControllingClient.SetChildAgentThrottle(cAgent.Throttles);

                m_headrotation = cAgent.HeadRotation;
                m_bodyRot = cAgent.BodyRotation;
                m_AgentControlFlags = (AgentManager.ControlFlags)cAgent.ControlFlags;

                //if (m_scene.Permissions.IsGod(new UUID(cAgent.AgentID)))
                //    m_godLevel = cAgent.GodLevel;
                m_speedModifier = cAgent.Speed;
                DrawDistance = cAgent.DrawDistance;
                m_setAlwaysRun = cAgent.AlwaysRun;
                m_InitialHasWearablesBeenSent = cAgent.SentInitialWearables;
                m_appearance = new AvatarAppearance(cAgent.Appearance);

                try
                {
                    lock (scriptedcontrols)
                    {
                        if (cAgent.Controllers != null)
                        {
                            scriptedcontrols.Clear();

                            foreach (ControllerData c in cAgent.Controllers)
                            {
                                ScriptControllers sc = new ScriptControllers();
                                sc.itemID = c.ItemID;
                                sc.ignoreControls = (ScriptControlled)c.IgnoreControls;
                                sc.eventControls = (ScriptControlled)c.EventControls;

                                scriptedcontrols[sc.itemID] = sc;
                            }
                        }
                    }
                }
                catch { }
                // Animations
                try
                {
                    Animator.ResetAnimations();
                    Animator.Animations.FromArray(cAgent.Anims);
                }
                catch { }
            }
            catch(Exception ex)
            {
                m_log.Warn("[ScenePresence]: Error in CopyFrom: " + ex.ToString());
            }
        }

        #endregion Child Agent Updates

        #region Physics

        public delegate void AddPhysics();
        public event AddPhysics OnAddPhysics;

        /// <summary>
        /// Adds a physical representation of the avatar to the Physics plugin
        /// </summary>
        public void AddToPhysicalScene(bool isFlying, bool AddAvHeightToPosition)
        {
            //Make sure we arn't already doing this
            if (m_creatingPhysicalRepresentation)
                return;

            //Set this so we don't do it multiple times
            m_creatingPhysicalRepresentation = true;

            if (m_appearance.AvatarHeight == 0)
                m_appearance.SetHeight();

            if (m_appearance.AvatarHeight != 0)
                m_avHeight = m_appearance.AvatarHeight;

            PhysicsScene scene = m_scene.SceneGraph.PhysicsScene;

            Vector3 pVec = AbsolutePosition;

            if(AddAvHeightToPosition) //This is here so that after teleports, you arrive just slightly higher so that you don't fall through the ground/objects
                pVec.Z += m_avHeight;

            m_physicsActor = scene.AddAvatar(Firstname + "." + Lastname, pVec, Rotation,
                                                 new Vector3(0f, 0f, m_appearance.AvatarHeight), isFlying);

            scene.AddPhysicsActorTaint(m_physicsActor);
            m_physicsActor.OnRequestTerseUpdate += SendTerseUpdateToAllClients;
            m_physicsActor.OnSignificantMovement += CheckForSignificantMovement;
            m_physicsActor.OnCollisionUpdate += PhysicsCollisionUpdate;
            m_physicsActor.OnPositionAndVelocityUpdate += PhysicsUpdatePosAndVelocity;

            m_physicsActor.OnOutOfBounds += OutOfBoundsCall; // Called for PhysicsActors when there's something wrong
            m_physicsActor.SubscribeEvents(500);
            m_physicsActor.LocalID = LocalId;
            m_physicsActor.Orientation = Rotation;

            //Tell any events about it
            if (OnAddPhysics != null)
                OnAddPhysics();

            //All done, reset this
            m_creatingPhysicalRepresentation = false;
        }

        private void OutOfBoundsCall(Vector3 pos)
        {
            //bool flying = m_physicsActor.Flying;
            //RemoveFromPhysicalScene();

            //AddToPhysicalScene(flying);
            if (ControllingClient != null)
                ControllingClient.SendAgentAlertMessage("Physics is having a problem with your avatar.  You may not be able to move until you relog.", true);
        }

        private void PhysicsUpdatePosAndVelocity()
        {
            //Whenever the physics engine updates its positions, we get this update and make sure the animator has the newest info
            if (Animator != null && m_parentID == UUID.Zero)
                Animator.UpdateMovementAnimations();
        }

        // Event called by the physics plugin to tell the avatar about a collision.
        private void PhysicsCollisionUpdate(EventArgs e)
        {
            if (e == null)
                return;

            CollisionEventUpdate collisionData = (CollisionEventUpdate)e;
            Dictionary<uint, ContactPoint> coldata = collisionData.m_objCollisionList;

            CollisionPlane = Vector4.UnitW;

            //Fire events for attachments
            IAttachmentsModule attModule = Scene.RequestModuleInterface<IAttachmentsModule>();
            if (attModule != null)
            {
                SceneObjectGroup[] attachments = attModule.GetAttachmentsForAvatar(UUID);
                foreach (SceneObjectGroup grp in attachments)
                {
                    grp.FireAttachmentCollisionEvents(e);
                }
            }

            List<uint> thisHitColliders = new List<uint>();
            List<uint> endedColliders = new List<uint>();
            List<uint> startedColliders = new List<uint>();

            // calculate things that started colliding this time
            // and build up list of colliders this time
            foreach (uint localid in coldata.Keys)
            {
                thisHitColliders.Add(localid);
                if (!m_lastColliders.Contains(localid))
                {
                    startedColliders.Add(localid);
                }
            }

            // calculate things that ended colliding
            foreach (uint localID in m_lastColliders)
            {
                if (!thisHitColliders.Contains(localID))
                {
                    endedColliders.Add(localID);
                }
            }

            //add the items that started colliding this time to the last colliders list.
            foreach (uint localID in startedColliders)
            {
                m_lastColliders.Add(localID);
                //Play collision sounds
                if (localID != 0 && CollisionSoundID == UUID.Zero && !IsChildAgent)
                {
                    CollisionSoundID = Sounds.OBJECT_COLLISION;
                }
            }

            // remove things that ended colliding from the last colliders list
            foreach (uint localID in endedColliders)
            {
                m_lastColliders.Remove(localID);
            }

            if (coldata.Count != 0 && Animator != null)
            {
                //If we are on the ground, we need to fix the collision plane for the avie (fixes their feet in the viewer)
                switch (Animator.CurrentMovementAnimation)
                {
                    case "STAND":
                    case "WALK":
                    case "RUN":
                    case "CROUCH":
                    case "CROUCHWALK":
                        {
                            ContactPoint lowest;
                            lowest.SurfaceNormal = Vector3.Zero;
                            lowest.Position = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                            lowest.Position.Z = Single.NaN;

                            //Find the lowest contact to use first
                            foreach (ContactPoint contact in coldata.Values)
                            {
                                if (Single.IsNaN(lowest.Position.Z) || contact.Position.Z != 0 && contact.Position.Z < lowest.Position.Z)
                                {
                                    lowest = contact;
                                }
                            }

                            //Then if the normal isn't zero, set it (if its zero, it tends to do odd things in the client)
                            if (lowest.Position != new Vector3(float.MaxValue, float.MaxValue, float.MaxValue))
                            {
                                Vector4 newPlane = new Vector4(-lowest.SurfaceNormal, -Vector3.Dot(lowest.Position, lowest.SurfaceNormal));
                                if (lowest.SurfaceNormal != Vector3.Zero)
                                    CollisionPlane = newPlane;
                            }

                            //No Zero vectors, as it causes bent knee in the client! Replace with <0, 0, 0, 1>
                            if (CollisionPlane == new Vector4(0, 0, 0, 0))
                                CollisionPlane = new Vector4(0, 0, 0, 1);
                        }
                        break;
                }
            }
        }

        private readonly List<uint> m_lastColliders = new List<uint>();

        public void PushForce(Vector3 impulse)
        {
            if (PhysicsActor != null)
            {
                PhysicsActor.AddForce(impulse, true);
            }
        }

        #endregion

        public void Close()
        {
            m_sceneViewer.Reset();

            RemoveFromPhysicalScene();
            if (m_animator == null)
                return;
            m_animator.Close();
            m_animator = null;
        }

        /// <summary>
        /// Tell the SceneViewer for the given client about the update
        /// </summary>
        /// <param name="presence"></param>
        /// <param name="flags"></param>
        public void AddUpdateToAvatar(SceneObjectPart part, PrimUpdateFlags flags)
        {
            m_sceneViewer.QueuePartForUpdate(part, flags);
        }

        #region Script Controls

        public ScriptControllers GetScriptControler(UUID itemID)
        {
            ScriptControllers takecontrols;

            lock (scriptedcontrols)
            {
                scriptedcontrols.TryGetValue(itemID, out takecontrols);
            }
            return takecontrols;
        }

        public void RegisterControlEventsToScript(int controls, int accept, int pass_on, SceneObjectPart part, UUID Script_item_UUID)
        {
            ScriptControllers obj = new ScriptControllers();
            obj.ignoreControls = ScriptControlled.CONTROL_ZERO;
            obj.eventControls = ScriptControlled.CONTROL_ZERO;

            obj.itemID = Script_item_UUID;
            obj.part = part;
            if (pass_on == 0 && accept == 0)
            {
                IgnoredControls |= (ScriptControlled)controls;
                obj.ignoreControls = (ScriptControlled)controls;
            }

            if (pass_on == 0 && accept == 1)
            {
                IgnoredControls |= (ScriptControlled)controls;
                obj.ignoreControls = (ScriptControlled)controls;
                obj.eventControls = (ScriptControlled)controls;
            }
            if (pass_on == 1 && accept == 1)
            {
                IgnoredControls = ScriptControlled.CONTROL_ZERO;
                obj.eventControls = (ScriptControlled)controls;
                obj.ignoreControls = ScriptControlled.CONTROL_ZERO;
            }

            lock (scriptedcontrols)
            {
                if (pass_on == 1 && accept == 0)
                {
                    IgnoredControls &= ~(ScriptControlled)controls;
                    if (scriptedcontrols.ContainsKey(Script_item_UUID))
                        scriptedcontrols.Remove(Script_item_UUID);
                }
                else
                {
                    scriptedcontrols[Script_item_UUID] = obj;
                }
            }
            ControllingClient.SendTakeControls(controls, pass_on == 1 ? true : false, true);
        }

        public void RegisterScriptController(ScriptControllers SC)
        {
            lock (scriptedcontrols)
            {
                scriptedcontrols[SC.itemID] = SC;
            }
            ControllingClient.SendTakeControls((int)SC.eventControls, true, true);
        }

        public void HandleForceReleaseControls(IClientAPI remoteClient, UUID agentID)
        {
            IgnoredControls = ScriptControlled.CONTROL_ZERO;
            lock (scriptedcontrols)
            {
                scriptedcontrols.Clear();
            }
            ControllingClient.SendTakeControls(int.MaxValue, false, false);
        }

        public void UnRegisterControlEventsToScript(uint Obj_localID, UUID Script_item_UUID)
        {
            ScriptControllers takecontrols;

            lock (scriptedcontrols)
            {
                if (scriptedcontrols.TryGetValue(Script_item_UUID, out takecontrols))
                {
                    ScriptControlled sctc = takecontrols.eventControls;

                    ControllingClient.SendTakeControls((int)sctc, false, false);
                    ControllingClient.SendTakeControls((int)sctc, true, false);

                    scriptedcontrols.Remove(Script_item_UUID);
                    IgnoredControls = ScriptControlled.CONTROL_ZERO;
                    foreach (ScriptControllers scData in scriptedcontrols.Values)
                    {
                        IgnoredControls |= scData.ignoreControls;
                    }
                }
            }
        }

        protected internal void SendControlToScripts(uint flags)
        {
            ScriptControlled allflags = ScriptControlled.CONTROL_ZERO;

            if (MouseDown)
            {
                allflags = LastCommands & (ScriptControlled.CONTROL_ML_LBUTTON | ScriptControlled.CONTROL_LBUTTON);
                if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_LBUTTON_UP) != 0 || (flags & unchecked((uint)AgentManager.ControlFlags.AGENT_CONTROL_ML_LBUTTON_UP)) != 0)
                {
                    allflags = ScriptControlled.CONTROL_ZERO;
                    MouseDown = true;
                }
            }

            if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_ML_LBUTTON_DOWN) != 0)
            {
                allflags |= ScriptControlled.CONTROL_ML_LBUTTON;
                MouseDown = true;
            }
            if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_LBUTTON_DOWN) != 0)
            {
                allflags |= ScriptControlled.CONTROL_LBUTTON;
                MouseDown = true;
            }

            // find all activated controls, whether the scripts are interested in them or not
            if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_AT_POS) != 0 || (flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_AT_POS) != 0)
            {
                allflags |= ScriptControlled.CONTROL_FWD;
            }
            if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_AT_NEG) != 0 || (flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_AT_NEG) != 0)
            {
                allflags |= ScriptControlled.CONTROL_BACK;
            }
            if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_UP_POS) != 0 || (flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_POS) != 0)
            {
                allflags |= ScriptControlled.CONTROL_UP;
            }
            if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_UP_NEG) != 0 || (flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_NEG) != 0)
            {
                allflags |= ScriptControlled.CONTROL_DOWN;
            }
            if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_LEFT_POS) != 0 || (flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_POS) != 0)
            {
                allflags |= ScriptControlled.CONTROL_LEFT;
            }
            if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_LEFT_NEG) != 0 || (flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_NEG) != 0)
            {
                allflags |= ScriptControlled.CONTROL_RIGHT;
            }
            if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_YAW_NEG) != 0)
            {
                allflags |= ScriptControlled.CONTROL_ROT_RIGHT;
            }
            if ((flags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_YAW_POS) != 0)
            {
                allflags |= ScriptControlled.CONTROL_ROT_LEFT;
            }
            // optimization; we have to check per script, but if nothing is pressed and nothing changed, we can skip that
            if (allflags != ScriptControlled.CONTROL_ZERO || allflags != LastCommands)
            {
                lock (scriptedcontrols)
                {
                    foreach (KeyValuePair<UUID, ScriptControllers> kvp in scriptedcontrols)
                    {
                        UUID scriptUUID = kvp.Key;
                        ScriptControllers scriptControlData = kvp.Value;

                        ScriptControlled localHeld = allflags & scriptControlData.eventControls;     // the flags interesting for us
                        ScriptControlled localLast = LastCommands & scriptControlData.eventControls; // the activated controls in the last cycle
                        ScriptControlled localChange = localHeld ^ localLast;                        // the changed bits
                        if (localHeld != ScriptControlled.CONTROL_ZERO || localChange != ScriptControlled.CONTROL_ZERO)
                        {
                            // only send if still pressed or just changed
                            m_scene.EventManager.TriggerControlEvent(scriptControlData.part, scriptUUID, UUID, (uint)localHeld, (uint)localChange);
                        }
                    }
                }
            }

            LastCommands = allflags;
        }

        protected internal static AgentManager.ControlFlags RemoveIgnoredControls(AgentManager.ControlFlags flags, ScriptControlled ignored)
        {
            if (ignored == ScriptControlled.CONTROL_ZERO)
                return flags;

            if ((ignored & ScriptControlled.CONTROL_BACK) != 0)
                flags &= ~(AgentManager.ControlFlags.AGENT_CONTROL_AT_NEG | AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_AT_NEG);
            if ((ignored & ScriptControlled.CONTROL_FWD) != 0)
                flags &= ~(AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_AT_POS | AgentManager.ControlFlags.AGENT_CONTROL_AT_POS);
            if ((ignored & ScriptControlled.CONTROL_DOWN) != 0)
                flags &= ~(AgentManager.ControlFlags.AGENT_CONTROL_UP_NEG | AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_NEG);
            if ((ignored & ScriptControlled.CONTROL_UP) != 0)
                flags &= ~(AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_POS | AgentManager.ControlFlags.AGENT_CONTROL_UP_POS);
            if ((ignored & ScriptControlled.CONTROL_LEFT) != 0)
                flags &= ~(AgentManager.ControlFlags.AGENT_CONTROL_LEFT_POS | AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_POS);
            if ((ignored & ScriptControlled.CONTROL_RIGHT) != 0)
                flags &= ~(AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_NEG | AgentManager.ControlFlags.AGENT_CONTROL_LEFT_NEG);
            if ((ignored & ScriptControlled.CONTROL_ROT_LEFT) != 0)
                flags &= ~(AgentManager.ControlFlags.AGENT_CONTROL_YAW_NEG);
            if ((ignored & ScriptControlled.CONTROL_ROT_RIGHT) != 0)
                flags &= ~(AgentManager.ControlFlags.AGENT_CONTROL_YAW_POS);
            if ((ignored & ScriptControlled.CONTROL_ML_LBUTTON) != 0)
                flags &= ~(AgentManager.ControlFlags.AGENT_CONTROL_ML_LBUTTON_DOWN);
            if ((ignored & ScriptControlled.CONTROL_LBUTTON) != 0)
                flags &= ~(AgentManager.ControlFlags.AGENT_CONTROL_LBUTTON_UP | AgentManager.ControlFlags.AGENT_CONTROL_LBUTTON_DOWN);

            //DIR_CONTROL_FLAG_FORWARD = AgentManager.ControlFlags.AGENT_CONTROL_AT_POS,
            //DIR_CONTROL_FLAG_BACK = AgentManager.ControlFlags.AGENT_CONTROL_AT_NEG,
            //DIR_CONTROL_FLAG_LEFT = AgentManager.ControlFlags.AGENT_CONTROL_LEFT_POS,
            //DIR_CONTROL_FLAG_RIGHT = AgentManager.ControlFlags.AGENT_CONTROL_LEFT_NEG,
            //DIR_CONTROL_FLAG_UP = AgentManager.ControlFlags.AGENT_CONTROL_UP_POS,
            //DIR_CONTROL_FLAG_DOWN = AgentManager.ControlFlags.AGENT_CONTROL_UP_NEG,
            //DIR_CONTROL_FLAG_DOWN_NUDGE = AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_NEG

            return flags;
        }

        #endregion

        #region Module Interfaces

        /// <summary>
        /// For the given interface, retrieve the region module which implements it.
        /// </summary>
        /// <returns>null if there is no registered module implementing that interface</returns>
        public T RequestModuleInterface<T>()
        {
            if (ModuleInterfaces.ContainsKey(typeof(T)) &&
                    (ModuleInterfaces[typeof(T)].Count > 0))
                return (T)ModuleInterfaces[typeof(T)][0];
            else
                return default(T);
        }

        /// <value>
        /// The module interfaces available from this scene presence.
        /// </value>
        protected Dictionary<Type, List<object>> ModuleInterfaces = new Dictionary<Type, List<object>>();

        /// <summary>
        /// Register an interface to a region module.  This allows module methods to be called directly as
        /// well as via events.  If there is already a module registered for this interface, it is not replaced
        /// (is this the best behaviour?)
        /// </summary>
        /// <param name="mod"></param>
        public void RegisterModuleInterface<M>(M mod)
        {
            //            m_log.DebugFormat("[SCENE BASE]: Registering interface {0}", typeof(M));

            List<Object> l = null;
            if (!ModuleInterfaces.TryGetValue(typeof(M), out l))
            {
                l = new List<Object>();
                ModuleInterfaces.Add(typeof(M), l);
            }

            if (l.Count > 0)
                return;

            l.Add(mod);
        }

        public void UnregisterModuleInterface<M>(M mod)
        {
            List<Object> l;
            if (ModuleInterfaces.TryGetValue(typeof(M), out l))
            {
                l.Remove(mod);
                ModuleInterfaces[typeof(M)] = l;
            }
        }

        #endregion
    }
}
