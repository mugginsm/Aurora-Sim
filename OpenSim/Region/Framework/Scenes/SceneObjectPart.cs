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
using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using log4net;
using OpenMetaverse;
using OpenMetaverse.Packets;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes.Serialization;
using OpenSim.Region.Framework.Scenes.Components;
using OpenSim.Region.Physics.Manager;
using OpenMetaverse.StructuredData;
using Aurora.Framework;

namespace OpenSim.Region.Framework.Scenes
{
    #region Enumerations

    /// <summary>
    /// Only used internally to schedule client updates.
    /// 0 - no update is scheduled
    /// 1 - terse update scheduled
    /// 2 - full update scheduled
    /// </summary>
    /// 
    public enum InternalUpdateFlags : byte
    {
        NoUpdate = 0,
        TerseUpdate = 1,
        FullUpdate = 2
    }

    [Flags]
    public enum Changed : uint
    {
        INVENTORY = 1,
        COLOR = 2,
        SHAPE = 4,
        SCALE = 8,
        TEXTURE = 16,
        LINK = 32,
        ALLOWED_DROP = 64,
        OWNER = 128,
        REGION = 256,
        TELEPORT = 512,
        REGION_RESTART = 1024,
        MEDIA = 2048,
        ANIMATION = 16384,
        STATE = 32768
    }

    // I don't really know where to put this except here.
    // Can't access the OpenSim.Region.ScriptEngine.Common.LSL_BaseClass.Changed constants
    [Flags]
    public enum ExtraParamType
    {
        Something1 = 1,
        Something2 = 2,
        Something3 = 4,
        Something4 = 8,
        Flexible = 16,
        Light = 32,
        Sculpt = 48,
        Something5 = 64,
        Something6 = 128
    }

    [Flags]
    public enum TextureAnimFlags : byte
    {
        NONE = 0x00,
        ANIM_ON = 0x01,
        LOOP = 0x02,
        REVERSE = 0x04,
        PING_PONG = 0x08,
        SMOOTH = 0x10,
        ROTATE = 0x20,
        SCALE = 0x40
    }

    public enum PrimType : int
    {
        BOX = 0,
        CYLINDER = 1,
        PRISM = 2,
        SPHERE = 3,
        TORUS = 4,
        TUBE = 5,
        RING = 6,
        SCULPT = 7
    }

    #endregion Enumerations

    public class SceneObjectPart : ISceneEntity
    {
        /// <value>
        /// Denote all sides of the prim
        /// </value>
        public const int ALL_SIDES = -1;
        
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <value>
        /// Is this sop a root part?
        /// </value>
        [XmlIgnore]
        public bool IsRoot 
        {
           get { return ParentGroup.RootPart == this; } 
        }

        // use only one serializer to give the runtime a chance to optimize it (it won't do that if you
        // use a new instance every time)
        //private static XmlSerializer serializer = new XmlSerializer(typeof (SceneObjectPart));

        #region Fields

        public bool AllowedDrop;

        [XmlIgnore]
        public bool DIE_AT_EDGE
        {
            get
            {
                return GetComponentState("DIE_AT_EDGE").AsBoolean();
            }
            set
            {
                SetComponentState("DIE_AT_EDGE", value);
            }
        }

        [XmlIgnore]
        public bool RETURN_AT_EDGE
        {
            get
            {
                return GetComponentState("RETURN_AT_EDGE").AsBoolean();
            }
            set
            {
                SetComponentState("RETURN_AT_EDGE", value);
            }
        }

        [XmlIgnore]
        public bool BlockGrab
        {
            get
            {
                return GetComponentState("BlockGrab").AsBoolean();
            }
            set
            {
                SetComponentState("BlockGrab", value);
            }
        }

        private bool m_IsLoading = false;
        [XmlIgnore]
        public bool IsLoading
        {
            get { return m_IsLoading; }
            set { m_IsLoading = value; }
        }

        [XmlIgnore]
        public bool StatusSandbox
        {
            get
            {
                return GetComponentState("StatusSandbox").AsBoolean();
            }
            set
            {
                SetComponentState("StatusSandbox", value);
            }
        }

        [XmlIgnore]
        public Vector3 StatusSandboxPos
        {
            get
            {
                return GetComponentState("StatusSandboxPos").AsVector3();
            }
            set
            {
                SetComponentState("StatusSandboxPos", value);
            }
        }

        [XmlIgnore]
        public int UseSoundQueue
        {
            get
            {
                return GetComponentState("UseSoundQueue").AsInteger();
            }
            set
            {
                SetComponentState("UseSoundQueue", value);
            }
        }

        // TODO: This needs to be persisted in next XML version update!
        [XmlIgnore]
        public readonly int[] PayPrice = {-2,-2,-2,-2,-2};

        [XmlIgnore]
        public PhysicsActor PhysActor
        {
            get { return m_physActor; }
            set
            {
//                m_log.DebugFormat("[SOP]: PhysActor set to {0} for {1} {2}", value, Name, UUID);
                m_physActor = value;
            }
        }

        [XmlIgnore]
        public UUID Sound
        {
            get
            {
                return GetComponentState("Sound").AsUUID();
            }
            set
            {
                SetComponentState("Sound", value);
            }
        }
        
        [XmlIgnore]
        public byte SoundFlags
        {
            get
            {
                return (byte)GetComponentState("Sound").AsInteger();
            }
            set
            {
                SetComponentState("SoundFlags", (int)value);
            }
        }
        
        [XmlIgnore]
        public double SoundGain
        {
            get
            {
                return GetComponentState("SoundGain").AsReal();
            }
            set
            {
                SetComponentState("SoundGain", value);
            }
        }

        [XmlIgnore]
        public double SoundRadius
        {
            get
            {
                return GetComponentState("SoundRadius").AsReal();
            }
            set
            {
                SetComponentState("SoundRadius", value);
            }
        }
        
        [XmlIgnore]
        public uint TimeStampLastActivity; // Will be used for AutoReturn
        
        [XmlIgnore]
        public UUID FromItemID;

        [XmlIgnore]
        public int STATUS_ROTATE_X
        {
            get
            {
                return GetComponentState("STATUS_ROTATE_X").AsInteger();
            }
            set 
            {
                SetComponentState("STATUS_ROTATE_X", value);
            }
        }

        [XmlIgnore]
        public int STATUS_ROTATE_Y
        {
            get
            {
                return GetComponentState("STATUS_ROTATE_Y").AsInteger();
            }
            set
            {
                SetComponentState("STATUS_ROTATE_Y", value);
            }
        }

        [XmlIgnore]
        public int STATUS_ROTATE_Z
        {
            get
            {
                return GetComponentState("STATUS_ROTATE_Z").AsInteger();
            }
            set
            {
                SetComponentState("STATUS_ROTATE_Z", value);
            }
        }

        //For Non Physical llMoveToTarget
        private Vector3 m_initialPIDLocation = Vector3.Zero;

        [XmlIgnore]
        public Vector3 PIDTarget
        {
            get
            {
                return GetComponentState("PIDTarget").AsVector3();
            }
            set
            {
                SetComponentState("PIDTarget", value);
                if (PhysActor != null)
                    PhysActor.PIDTarget = value;
            }
        }

        [XmlIgnore]
        public bool PIDActive
        {
            get
            {
                return GetComponentState("PIDActive").AsBoolean();
            }
            set
            {
                SetComponentState("PIDActive", value);
                if(PhysActor != null)
                    PhysActor.PIDActive = value;
            }
        }

        [XmlIgnore]
        public float PIDTau
        {
            get
            {
                return (float)GetComponentState("PIDTau").AsReal();
            }
            set
            {
                SetComponentState("PIDTau", value);
                if (PhysActor != null)
                    PhysActor.PIDTau = value;
            }
        }
        
        [XmlIgnore]
        private Dictionary<int, string> m_CollisionFilter = new Dictionary<int, string>();
               
        /// <value>
        /// The UUID of the user inventory item from which this object was rezzed if this is a root part.
        /// If UUID.Zero then either this is not a root part or there is no connection with a user inventory item.
        /// </value>
        private UUID m_fromUserInventoryItemID;
        
        [XmlIgnore]
        public UUID FromUserInventoryItemID
        {
            get { return m_fromUserInventoryItemID; }
            set { m_fromUserInventoryItemID = value; }
        }

        [XmlIgnore]
        public bool IsAttachment;

        [XmlIgnore]
        public scriptEvents AggregateScriptEvents;

        [XmlIgnore]
        public UUID AttachedAvatar;

        private Vector3 m_AttachedPos;
        [XmlIgnore]
        public Vector3 AttachedPos
        {
            get
            {
                return m_AttachedPos;
            }
            set
            {
                m_AttachedPos = value;
            }
        }

        private int m_AttachmentPoint;
        /// <summary>
        /// NOTE: THIS WILL NOT BE UP TO DATE AS THEY WILL BE ONE REV BEHIND
        /// Used to save attachment pos and point over rezzing/taking
        /// </summary>
        [XmlIgnore]
        public int AttachmentPoint
        {
            get
            {
                return m_AttachmentPoint;
            }
            set
            {
                m_AttachmentPoint = value;
            }
        }

        private Vector3 m_SavedAttachedPos;
        /// <summary>
        /// NOTE: THIS WILL NOT BE UP TO DATE AS THEY WILL BE ONE REV BEHIND
        /// Used to save attachment pos and point over rezzing/taking
        /// </summary>
        public Vector3 SavedAttachedPos
        {
            get
            {
                return GetComponentState("SavedAttachedPos").AsVector3();
            }
            set
            {
                SetComponentState("SavedAttachedPos", value);
                m_SavedAttachedPos = value;
            }
        }

        [XmlIgnore]
        private bool m_IsSelected=false;

        private int m_savedAttachmentPoint;
        public int SavedAttachmentPoint
        {
            get
            {
                return GetComponentState("SavedAttachmentPoint").AsInteger();
            }
            set
            {
                SetComponentState("SavedAttachmentPoint", value);
                m_savedAttachmentPoint = value;
            }
        }

        [XmlIgnore]
        public Vector3 RotationAxis = Vector3.One;

        [XmlIgnore]
        public bool VolumeDetectActive
        {
            get
            {
                return GetComponentState("VolumeDetectActive").AsBoolean();
            }
            set
            {
                SetComponentState("VolumeDetectActive", value);
            }
        }

        [XmlIgnore]
        public bool IsWaitingForFirstSpinUpdatePacket;

        [XmlIgnore]
        public Quaternion SpinOldOrientation = Quaternion.Identity;

        [XmlIgnore]
        public Quaternion m_APIDTarget = Quaternion.Identity;

        [XmlIgnore]
        public float m_APIDDamp = 0;

        [XmlIgnore]
        public float m_APIDStrength = 0;

        /// <summary>
        /// This part's inventory
        /// </summary>
        [XmlIgnore]
        public IEntityInventory Inventory
        {
            get { return m_inventory; }
        }
        protected SceneObjectPartInventory m_inventory;

        [XmlIgnore]
        public bool Undoing;

        [XmlIgnore]
        public bool IgnoreUndoUpdate = false;

        [XmlIgnore]
        private PrimFlags LocalFlags;
        private byte m_clickAction;
        private Color m_color = Color.Black;
        private string m_description = String.Empty;
        private readonly List<uint> m_lastColliders = new List<uint>();
        private int m_linkNum;
        [XmlIgnore]
        private int m_scriptAccessPin;
        [XmlIgnore]
        private Dictionary<UUID, scriptEvents> m_scriptEvents = new Dictionary<UUID, scriptEvents>();
        private string m_sitName = String.Empty;
        private string m_sitAnimation = "SIT";
        private string m_text = String.Empty;
        private string m_touchName = String.Empty;
        private UndoStack<UndoState> m_undo = new UndoStack<UndoState>(5);
        private UndoStack<UndoState> m_redo = new UndoStack<UndoState>(5);
        private UUID _creatorID;

        private int m_passTouches;

        private PhysicsActor m_physActor;
        protected Vector3 m_acceleration;
        protected Vector3 m_angularVelocity;

        //unknown if this will be kept, added as a way of removing the group position from the group class
        protected Vector3 m_groupPosition;
        protected uint m_localId;
        protected uint m_crc;
        protected Material m_material = OpenMetaverse.Material.Wood;
        protected string m_name;
        protected Vector3 m_offsetPosition;

        // FIXME, TODO, ERROR: 'ParentGroup' can't be in here, move it out.
        protected SceneObjectGroup m_parentGroup;
        protected byte[] m_particleSystem = Utils.EmptyBytes;
        protected ulong m_regionHandle;
        protected Quaternion m_rotationOffset = Quaternion.Identity;
        protected PrimitiveBaseShape m_shape;
        protected UUID m_uuid;

        protected Vector3 m_lastPosition;
        protected Vector3 m_lastGroupPosition;
        protected Quaternion m_lastRotation;
        protected Vector3 m_lastVelocity;
        protected Vector3 m_lastAcceleration;
        protected Vector3 m_lastAngularVelocity;
        /// <summary>
        /// This scene is set from the constructor and will be right as long as the object does not leave the region, this is to be able to access the Scene while starting up
        /// </summary>
        private IRegistryCore m_initialScene;
        
        /// <summary>
        /// Stores media texture data
        /// </summary>
        protected string m_mediaUrl;

        public Vector3 CameraEyeOffset
        {
            get
            {
                return GetComponentState("CameraEyeOffset").AsVector3();
            }
            set
            {
                SetComponentState("CameraEyeOffset", value);
            }
        }

        public Vector3 CameraAtOffset
        {
            get
            {
                return GetComponentState("CameraAtOffset").AsVector3();
            }
            set
            {
                SetComponentState("CameraAtOffset", value);
            }
        }

        public bool ForceMouselook
        {
            get
            {
                return GetComponentState("ForceMouselook").AsBoolean();
            }
            set
            {
                SetComponentState("ForceMouselook", value);
            }
        }
        
        private UUID m_collisionSound;
        private UUID m_collisionSprite;
        private float m_collisionSoundVolume;

        [XmlIgnore]
        public string GenericData
        {
            get
            {
                string data = string.Empty;
                //Get the Components from the ComponentManager
                IComponentManager manager = (ParentGroup == null ? m_initialScene : ParentGroup.Scene).RequestModuleInterface<IComponentManager>();
                if (manager != null)
                    data = manager.SerializeComponents(this);
                return data;
            }
            set
            {
                //Set the Components for this object
                IComponentManager manager = (ParentGroup == null ? m_initialScene : ParentGroup.Scene).RequestModuleInterface<IComponentManager>();
                if (manager != null)
                    manager.DeserializeComponents(this, value);
            }
        }

        /// <summary>
        /// Get the current State of a Component
        /// </summary>
        /// <param name="Name"></param>
        /// <returns></returns>
        public OSD GetComponentState(string Name)
        {
            IComponentManager manager = (ParentGroup == null ? m_initialScene : ParentGroup.Scene).RequestModuleInterface<IComponentManager>();
            if (manager != null)
                return manager.GetComponentState(this, Name);

            return null;
        }

        /// <summary>
        /// Set a Component with the given name's State
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="State"></param>
        public void SetComponentState(string Name, object State)
        {
            SetComponentState(Name, State, true);
        }

        /// <summary>
        /// Set a Component with the given name's State
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="State"></param>
        /// <param name="shouldBackup">Should this be backed up now</param>
        public void SetComponentState(string Name, object State, bool shouldBackup)
        {
            if (IsLoading) //No saving while loading
                return;
            //Back up the object later
            if (ParentGroup != null && shouldBackup)
                ParentGroup.HasGroupChanged = true;

            //Tell the ComponentManager about it
            IComponentManager manager = (ParentGroup == null ? m_initialScene : ParentGroup.Scene) == null ? null : (ParentGroup == null ? m_initialScene : ParentGroup.Scene).RequestModuleInterface<IComponentManager>();
            if (manager != null)
            {
                OSD state = (State is OSD) ? (OSD)State : OSD.FromObject(State);
                manager.SetComponentState(this, Name, state);
            }
        }

        public void ResetComponentsToNewID(UUID oldID)
        {
            if (oldID == UUID.Zero)
                return;
            if (IsLoading)
                return;
            IComponentManager manager = (ParentGroup == null ? m_initialScene : ParentGroup.Scene) == null ? null : (ParentGroup == null ? m_initialScene : ParentGroup.Scene).RequestModuleInterface<IComponentManager>();
            if (manager != null)
            {
                manager.ResetComponentIDsToNewObject(oldID, this);
            }
        }

        #endregion Fields

        #region Constructors

        /// <summary>
        /// No arg constructor called by region restore db code
        /// </summary>
        public SceneObjectPart()
        {
        }

        public SceneObjectPart(IRegistryCore scene)
        {
            // It's not necessary to persist this
            m_initialScene = scene;

            m_inventory = new SceneObjectPartInventory(this);
        }

        /// <summary>
        /// Create a completely new SceneObjectPart (prim).  This will need to be added separately to a SceneObjectGroup
        /// </summary>
        /// <param name="ownerID"></param>
        /// <param name="shape"></param>
        /// <param name="position"></param>
        /// <param name="rotationOffset"></param>
        /// <param name="offsetPosition"></param>
        public SceneObjectPart(
            UUID ownerID, PrimitiveBaseShape shape, Vector3 groupPosition,
            Quaternion rotationOffset, Vector3 offsetPosition, Scene scene)
        {
            m_name = scene.DefaultObjectName;
            m_initialScene = scene;

            _creationDate = (int)Utils.DateTimeToUnixTime(DateTime.Now);
            _ownerID = ownerID;
            _creatorID = _ownerID;
            _lastOwnerID = UUID.Zero;
            UUID = UUID.Random();
            Shape = shape;
            CRC = 0;
            _ownershipCost = 0;
            _flags = 0;
            _groupID = UUID.Zero;
            _objectSaleType = 0;
            _salePrice = 0;
            _category = 0;
            _lastOwnerID = _creatorID;
            m_groupPosition=groupPosition;
            m_offsetPosition = offsetPosition;
            RotationOffset = rotationOffset;
            Velocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
            Acceleration = Vector3.Zero;

            // Prims currently only contain a single folder (Contents).  From looking at the Second Life protocol,
            // this appears to have the same UUID (!) as the prim.  If this isn't the case, one can't drag items from
            // the prim into an agent inventory (Linden client reports that the "Object not found for drop" in its log

            Flags = 0;
            CreateSelected = true;

            TrimPermissions();
            //m_undo = new UndoStack<UndoState>(ParentGroup.GetSceneMaxUndo());
            
            m_inventory = new SceneObjectPartInventory(this);
        }

        #endregion Constructors

        #region XML Schema

        private UUID _lastOwnerID;
        private UUID _ownerID;
        private UUID _groupID;
        private int _ownershipCost;
        private byte _objectSaleType;
        private int _salePrice;
        private uint _category;
        private Int32 _creationDate;
        private uint _parentID = 0;
        private List<UUID> m_sitTargetAvatar = new List<UUID>();
        private uint _baseMask = (uint)PermissionMask.All;
        private uint _ownerMask = (uint)PermissionMask.All;
        private uint _groupMask = (uint)PermissionMask.None;
        private uint _everyoneMask = (uint)PermissionMask.None;
        private uint _nextOwnerMask = (uint)PermissionMask.All;
        private PrimFlags _flags = PrimFlags.None;
        private bool m_createSelected = false;
        private string m_currentMediaVersion = "x-mv:0000000001/00000000-0000-0000-0000-000000000000";
        [XmlIgnore]
        public string CurrentMediaVersion
        {
            get { return m_currentMediaVersion; }
            set { m_currentMediaVersion = value; }
        }

        public UUID CreatorID 
        {
            get
            {
                return _creatorID;
            }
            set
            {
                _creatorID = value;
            }
        }

        /// <summary>
        /// A relic from when we we thought that prims contained folder objects. In 
        /// reality, prim == folder
        /// Exposing this is not particularly good, but it's one of the least evils at the moment to see
        /// folder id from prim inventory item data, since it's not (yet) actually stored with the prim.
        /// </summary>
        public UUID FolderID
        {
            get { return UUID; }
            set { } // Don't allow assignment, or legacy prims wil b0rk - but we need the setter for legacy serialization.
        }

        /// <value>
        /// Access should be via Inventory directly - this property temporarily remains for xml serialization purposes
        /// </value>
        public uint InventorySerial
        {
            get { return m_inventory.Serial; }
            set { m_inventory.Serial = value; }
        }

        /// <value>
        /// Access should be via Inventory directly - this property temporarily remains for xml serialization purposes
        /// </value>
        public TaskInventoryDictionary TaskInventory
        {
            get { return m_inventory.Items; }
            set { m_inventory.Items = value; }
        }

        /// <summary>
        /// This is idential to the Flags property, except that the returned value is uint rather than PrimFlags
        /// </summary>
        [Obsolete("Use Flags property instead")]
        public uint ObjectFlags
        {
            get { return (uint)Flags; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                Flags = (PrimFlags)value;
            }
        }

        public UUID UUID
        {
            get { return m_uuid; }
            set 
            {
                UUID oldID = m_uuid;
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_uuid = value; 
                
                // This is necessary so that TaskInventoryItem parent ids correctly reference the new uuid of this part
                if (Inventory != null)
                    Inventory.ResetObjectID();

                ResetComponentsToNewID(oldID);
            }
        }

        public uint LocalId
        {
            get 
            {
                if(m_localId == 0)
                    m_localId = GetComponentState("LocalId").AsUInteger();
                return m_localId; 
            }
            set
            {
                m_localId = value;
                SetComponentState("LocalId", value, true);
            }
        }

        [XmlIgnore]
        public uint CRC
        {
            get
            {
                return GetComponentState("CRC").AsUInteger();
            }
            set
            {
                SetComponentState("CRC", value, false);
            }
        }

        public virtual string Name
        {
            get { return m_name; }
            set 
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_name = value;
                if (PhysActor != null)
                {
                    PhysActor.SOPName = value;
                }
            }
        }

        public byte Material
        {
            get { return (byte) m_material; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_material = (Material)value;
                if (PhysActor != null)
                {
                    PhysActor.SetMaterial((int)value);
                }
            }
        }

        [XmlIgnore]
        public bool PassTouches //Needed for compat, otherwise assets break!
        {
            get { return PassTouch == 1 || PassTouch == 2; }
            set
            {
                if (value)
                    PassTouch = 1;
                else
                    PassTouch = 0;
           }
        }

        public int PassTouch
        {
            get { return m_passTouches; }
            set
            {
                m_passTouches = value;
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
            }
        }

        private int m_passCollision;

        [XmlIgnore]
        public bool PassCollision //Needed for compat, otherwise assets break!
        {
            get { return m_passCollision == 1 || m_passCollision == 2; }
            set
            {
                if (value)
                    m_passCollision = 1;
                else
                    m_passCollision = 0;
            }
        }

        public int PassCollisions
        {
            get { return m_passCollision; }
            set
            {
                if(ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_passCollision = value;
            }
        }

        
        [XmlIgnore]
        public Dictionary<int, string> CollisionFilter
        {
            get { return m_CollisionFilter; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_CollisionFilter = value;
            }
        }

        [XmlIgnore]
        public Quaternion APIDTarget
        {
            get
            {
                return GetComponentState("APIDTarget").AsQuaternion();
            }
            set
            {
                SetComponentState("APIDTarget", value);
                m_APIDTarget = value;
            }
        }

        [XmlIgnore]
        public float APIDDamp
        {
            get 
            {
                return (float)GetComponentState("APIDDamp").AsReal();
            }
            set
            {
                SetComponentState("APIDDamp", value);
                m_APIDDamp = value; 
            }
        }

        [XmlIgnore]
        public float APIDStrength
        {
            get
            {
                return (float)GetComponentState("APIDStrength").AsReal();
            }
            set 
            {
                SetComponentState("APIDStrength", value);
                m_APIDStrength = value; 
            }
        }

        public int ScriptAccessPin
        {
            get { return m_scriptAccessPin; }
            set { m_scriptAccessPin = (int)value; }
        }

        private SceneObjectPart m_PlaySoundMasterPrim = null;
        [XmlIgnore]
        public SceneObjectPart PlaySoundMasterPrim
        {
            get { return m_PlaySoundMasterPrim; }
            set { m_PlaySoundMasterPrim = value; }
        }

        private List<SceneObjectPart> m_PlaySoundSlavePrims = new List<SceneObjectPart>();
        [XmlIgnore]
        public List<SceneObjectPart> PlaySoundSlavePrims
        {
            get { return m_PlaySoundSlavePrims; }
            set { m_PlaySoundSlavePrims = value; }
        }

        private SceneObjectPart m_LoopSoundMasterPrim = null;
        [XmlIgnore]
        public SceneObjectPart LoopSoundMasterPrim
        {
            get { return m_LoopSoundMasterPrim; }
            set { m_LoopSoundMasterPrim = value; }
        }

        private List<SceneObjectPart> m_LoopSoundSlavePrims = new List<SceneObjectPart>();
        [XmlIgnore]
        public List<SceneObjectPart> LoopSoundSlavePrims
        {
            get { return m_LoopSoundSlavePrims; }
            set { m_LoopSoundSlavePrims = value; }
        }

        public Byte[] TextureAnimation
        {
            get { return GetComponentState("TextureAnimation").AsBinary(); }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                SetComponentState("TextureAnimation", value);
            }
        }

        [XmlIgnore]
        public Byte[] ParticleSystem
        {
            get
            {
                return GetComponentState("ParticleSystem").AsBinary();
            }
            set
            {
                //MUST set via the OSD
                SetComponentState("ParticleSystem", OSD.FromBinary(value));
            }
        }

        [XmlIgnore]
        public DateTime Expires
        {
            get
            {
                return GetComponentState("Expires").AsDate();
            }
            set
            {
                SetComponentState("Expires", value);
            }
        }

        [XmlIgnore]
        public DateTime Rezzed
        {
            get 
            {
                return GetComponentState("Rezzed").AsDate();
            }
            set
            {
                SetComponentState("Rezzed", value);
            }
        }

        [XmlIgnore]
        public float Damage
        {
            get
            {
                return (float)GetComponentState("Damage").AsReal();
            }
            set
            {
                SetComponentState("Damage", value);
            }
        }

        /// <summary>
        /// The position of the entire group that this prim belongs to.
        /// </summary>
        public Vector3 GroupPosition
            {
            get
            {
                // If this is a linkset, we don't want the physics engine mucking up our group position here.
                PhysicsActor actor = PhysActor;
                if (actor != null && _parentID == 0)
                {
                    m_groupPosition = actor.Position;
                }

                if (IsAttachment)
                {
                    ScenePresence sp = m_parentGroup.Scene.GetScenePresence(AttachedAvatar);
                    if (sp != null)
                        return sp.AbsolutePosition;
                }

                return m_groupPosition;
            }            
            }



        public Vector3 OffsetPosition
        {
            get { return m_offsetPosition; }
  
        }

        public Vector3 RelativePosition
        {
            get
            {
                if (IsRoot)
                {
                    if (IsAttachment)
                        return AttachedPos;
                    else
                        return AbsolutePosition;
                }
                else
                {
                    return OffsetPosition;
                }
            }
        }

        public Quaternion RotationOffset
            {
            get
                {
                // We don't want the physics engine mucking up the rotations in a linkset
                PhysicsActor actor = PhysActor;
                if (_parentID == 0 && (Shape.PCode != 9 || Shape.State == 0) && actor != null)
                    {
                    if (actor.Orientation.X != 0f || actor.Orientation.Y != 0f
                        || actor.Orientation.Z != 0f || actor.Orientation.W != 0f)
                        {
                        m_rotationOffset = actor.Orientation;
                        }
                    }

                return m_rotationOffset;
                }
            set
                {
                SetRotationOffset(true, value, true);
                }
            }

        /// <summary></summary>
        public Vector3 Velocity
        {
            get
            {
                PhysicsActor actor = PhysActor;
                if (actor != null)
                {
                    if (actor.IsPhysical)
                    {
                        return actor.Velocity;
                    }
                }

                return Vector3.Zero;
            }

            set
            {
                PhysicsActor actor = PhysActor;
                if (actor != null)
                {
                    if (actor.IsPhysical)
                    {
                        actor.Velocity = value;
                        m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPhysicsActorTaint(actor);
                    }
                }
            }
        }

        /// <summary></summary>
        public Vector3 AngularVelocity
        {
            get
            {
                PhysicsActor actor = PhysActor;
                if ((actor != null) && actor.IsPhysical)
                {
                    m_angularVelocity = actor.RotationalVelocity;
                }
                return m_angularVelocity;
            }
            set
            {
                m_angularVelocity = value;
            }
        }

        /// <summary></summary>
        public Vector3 Acceleration
        {
            get { return m_acceleration; }
            set
            {
                m_acceleration = value;
            }
        }

        public string Description
        {
            get { return m_description; }
            set 
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_description = value;
                PhysicsActor actor = PhysActor;
                if (actor != null)
                {
                    actor.SOPDescription = value;
                }
            }
        }
        #region Only used for serialization as Color cannot be serialized
        public int ColorA
        {
            get { return m_color.A; }
            set
            {
                m_color = System.Drawing.Color.FromArgb(value, m_color.R, m_color.G, m_color.B);
            }
        }
        public int ColorR
        {
            get { return m_color.R; }
            set
            {
                m_color = System.Drawing.Color.FromArgb(m_color.A, value, m_color.G, m_color.B);
            }
        }
        public int ColorG
        {
            get { return m_color.G; }
            set
            {
                m_color = System.Drawing.Color.FromArgb(m_color.A, m_color.R, value, m_color.B);
            }
        }
        public int ColorB
        {
            get { return m_color.B; }
            set
            {
                m_color = System.Drawing.Color.FromArgb(m_color.A, m_color.R, m_color.G, value);
            }
        }

        #endregion

        /// <value>
        /// Text color.
        /// </value>
        [XmlIgnore]
        public Color Color
        {
            get { return m_color; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_color = value;
                TriggerScriptChangedEvent(Changed.COLOR);

                /* ScheduleFullUpdate() need not be called b/c after
                 * setting the color, the text will be set, so then
                 * ScheduleFullUpdate() will be called. */
                //ScheduleFullUpdate();
            }
        }

        public string Text
        {
            get
            {
                string returnstr = m_text;
                if (returnstr.Length > 255)
                {
                    returnstr = returnstr.Substring(0, 254);
                }
                return returnstr;
            }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_text = value;
            }
        }


        public string SitName
        {
            get { return m_sitName; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_sitName = value;
            }
        }

        public string TouchName
        {
            get { return m_touchName; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_touchName = value;
            }
        }

        public int LinkNum
        {
            get { return m_linkNum; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_linkNum = value;
            }
        }

        public byte ClickAction
        {
            get { return m_clickAction; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_clickAction = value;
            }
        }

        public PrimitiveBaseShape Shape
        {
            get { return m_shape; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                bool shape_changed = false;
                // TODO: this should really be restricted to the right
                // set of attributes on shape change.  For instance,
                // changing the lighting on a shape shouldn't cause
                // this.
                if (m_shape != null)
                    shape_changed = true;

                m_shape = value;

                if (shape_changed)
                    TriggerScriptChangedEvent(Changed.SHAPE);
            }
        }

        public Vector3 Scale
        {
            get { return m_shape.Scale; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                if (m_shape != null)
                {
                    if (m_shape.Scale != value)
                    {
                        StoreUndoState();

                        m_shape.Scale = value;

                        PhysicsActor actor = PhysActor;
                        if (actor != null && m_parentGroup != null)
                        {
                            if (m_parentGroup.Scene != null)
                            {
                                if (m_parentGroup.Scene.SceneGraph.PhysicsScene != null)
                                {
                                    actor.Size = m_shape.Scale;
                                    m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPhysicsActorTaint(actor);
                                }
                            }
                        }
                        TriggerScriptChangedEvent(Changed.SCALE);
                    }
                }
            }
        }
        
        /// <summary>
        /// Used for media on a prim.
        /// </summary>
        /// Do not change this value directly - always do it through an IMoapModule.
        public string MediaUrl 
        { 
            get
            {
                return m_mediaUrl; 
            }
            
            set
            {
                m_mediaUrl = value;
                
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
            }
        }

        [XmlIgnore]
        public bool CreateSelected
            {
            get { return m_createSelected; }
            set
                {
                //                m_log.DebugFormat("[SOP]: Setting CreateSelected to {0} for {1} {2}", value, Name, UUID);
                m_createSelected = value;
                }
            }

        [XmlIgnore]
        public bool IsSelected
        {
            get { return m_IsSelected; }
            set
            {
                if (m_IsSelected != value)
                {
                    if (PhysActor != null)
                    {
                        PhysActor.Selected = value;
                    }
                    if (ParentID != 0 && ParentGroup != null &&
                        ParentGroup.RootPart != null && ParentGroup.RootPart != this &&
                        ParentGroup.RootPart.IsSelected != value)
                        ParentGroup.RootPart.IsSelected = value;

                    m_IsSelected = value;
                }
            }
        }

        #endregion

        //---------------
        #region Public Properties with only Get

        public Vector3 AbsolutePosition
        {
            get {
                if (IsAttachment)
                    return GroupPosition;

                return GetWorldPosition(); }
        }

        public SceneObjectGroup ParentGroup
        {
            get { return m_parentGroup; }
        }

        public scriptEvents ScriptEvents
        {
            get { return AggregateScriptEvents; }
        }

        public Quaternion SitTargetOrientation
        {
            get
            {
                return GetComponentState("SitTargetOrientation").AsQuaternion();
            }
            set 
            {
                SetComponentState("SitTargetOrientation", value);
            }
        }


        public Vector3 SitTargetPosition
        {
            get
            {
                return GetComponentState("SitTargetPosition").AsVector3();
            }
            set
            {
                SetComponentState("SitTargetPosition", value);
            }
        }

        // This sort of sucks, but I'm adding these in to make some of
        // the mappings more consistant.
        public Vector3 SitTargetPositionLL
        {
            get
            {
                return GetComponentState("SitTargetPosition").AsVector3();
            }
            set
            {
                SetComponentState("SitTargetPosition", value);
            }
        }

        public Quaternion SitTargetOrientationLL
        {
            get
            {
                return GetComponentState("SitTargetOrientationLL").AsQuaternion();
            }

            set
            {
                SetComponentState("SitTargetOrientationLL", value);
            }
        }

        public bool Stopped
        {
            get {
                double threshold = 0.02;
                return (Math.Abs(Velocity.X) < threshold &&
                        Math.Abs(Velocity.Y) < threshold &&
                        Math.Abs(Velocity.Z) < threshold &&
                        Math.Abs(AngularVelocity.X) < threshold &&
                        Math.Abs(AngularVelocity.Y) < threshold &&
                        Math.Abs(AngularVelocity.Z) < threshold);
            }
        }

        public uint ParentID
        {
            get { return _parentID; }
            set { _parentID = value; }
        }

        public int CreationDate
        {
            get { return _creationDate; }
            set { _creationDate = value; }
        }

        public uint Category
        {
            get { return _category; }
            set { _category = value; }
        }

        public int SalePrice
        {
            get { return _salePrice; }
            set { _salePrice = value; }
        }

        public byte ObjectSaleType
        {
            get { return _objectSaleType; }
            set { _objectSaleType = value; }
        }

        public int OwnershipCost
        {
            get { return _ownershipCost; }
            set { _ownershipCost = value; }
        }

        public UUID GroupID
        {
            get { return _groupID; }
            set { _groupID = value; }
        }

        public UUID OwnerID
        {
            get { return _ownerID; }
            set { _ownerID = value; }
        }

        public UUID LastOwnerID
        {
            get { return _lastOwnerID; }
            set { _lastOwnerID = value; }
        }

        public uint BaseMask
        {
            get { return _baseMask; }
            set { _baseMask = value; }
        }

        public uint OwnerMask
        {
            get { return _ownerMask; }
            set { _ownerMask = value; }
        }

        public uint GroupMask
        {
            get { return _groupMask; }
            set { _groupMask = value; }
        }

        public uint EveryoneMask
        {
            get { return _everyoneMask; }
            set { _everyoneMask = value; }
        }

        public uint NextOwnerMask
        {
            get { return _nextOwnerMask; }
            set { _nextOwnerMask = value; }
        }

        /// <summary>
        /// Property flags.  See OpenMetaverse.PrimFlags 
        /// </summary>
        /// Example properties are PrimFlags.Phantom and PrimFlags.DieAtEdge
        public PrimFlags Flags
        {
            get { return _flags; }
            set 
            { 
//                m_log.DebugFormat("[SOP]: Setting flags for {0} {1} to {2}", UUID, Name, value);
                //if (ParentGroup != null && _flags != value)
                //    ParentGroup.HasGroupChanged = true;
                _flags = value; 
            }
        }

        [XmlIgnore]
        public List<UUID> SitTargetAvatar
        {
            get { return m_sitTargetAvatar; }
            set { m_sitTargetAvatar = value; }
        }

        [XmlIgnore]
        public virtual UUID RegionID
        {
            get
            {
                if (ParentGroup != null && ParentGroup.Scene != null)
                    return ParentGroup.Scene.RegionInfo.RegionID;
                else
                    return UUID.Zero;
            }
            set {} // read only
        }

        private UUID _parentUUID = UUID.Zero;
        [XmlIgnore]
        public UUID ParentUUID
        {
            get
            {
                if (ParentGroup != null)
                {
                    _parentUUID = ParentGroup.UUID;
                }
                return _parentUUID;
            }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                _parentUUID = value;
            }
        }

        [XmlIgnore]
        public string SitAnimation
        {
            get { return m_sitAnimation; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_sitAnimation = value;
            }
        }

        public UUID CollisionSound
        {
            get { return m_collisionSound; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_collisionSound = value;
                //Why?
                //aggregateScriptEvents();
            }
        }

        public UUID CollisionSprite
        {
            get { return m_collisionSprite; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_collisionSprite = value;
            }
        }

        public float CollisionSoundVolume
        {
            get { return m_collisionSoundVolume; }
            set
            {
                if (ParentGroup != null)
                    ParentGroup.HasGroupChanged = true;
                m_collisionSoundVolume = value;
            }
        }

        #endregion Public Properties with only Get

        #region Private Methods

        private uint ApplyMask(uint val, bool set, uint mask)
        {
            if (set)
            {
                return val |= mask;
            }
            else
            {
                return val &= ~mask;
            }
        }

        private void SendObjectPropertiesToClient(UUID AgentID)
        {
            ScenePresence SP = ParentGroup.Scene.GetScenePresence(AgentID);
            if(SP != null)
                m_parentGroup.GetProperties(SP.ControllingClient);
        }

        #endregion Private Methods

        #region Public Methods


        public void SetRotationOffset(bool UpdatePrimActor, Quaternion value, bool single)
            {
            if (ParentGroup != null)
                ParentGroup.HasGroupChanged = true;
            m_rotationOffset = value;

            if (value.W == 0) //We have an issue here... try to normalize it
                value.Normalize();

            PhysicsActor actor = PhysActor;
            if (actor != null)
                {
                if (actor.PhysicsActorType != (int)ActorTypes.Prim)  // for now let other times get updates
                    {
                    UpdatePrimActor = true;
                    single = false;
                    }
                if (UpdatePrimActor)
                    {
                    try
                        {
                        // Root prim gets value directly
                        if (_parentID == 0)
                            {
                            actor.Orientation = value;
                            //m_log.Info("[PART]: RO1:" + actor.Orientation.ToString());
                            }
                        else if (single || !actor.IsPhysical)
                            {
                            // Child prim we have to calculate it's world rotationwel
                            Quaternion resultingrotation = GetWorldRotation();
                            actor.Orientation = resultingrotation;
                            //m_log.Info("[PART]: RO2:" + actor.Orientation.ToString());
                            }
                        m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPhysicsActorTaint(actor);
                        //}
                        }
                    catch (Exception ex)
                        {
                        m_log.Error("[SCENEOBJECTPART]: ROTATIONOFFSET" + ex.Message);
                        }
                    }
                }

            }

        public void SetOffsetPosition(Vector3 value)
            {
            m_offsetPosition = value;
            }

        public void FixOffsetPosition(Vector3 value, bool single)
            {
            bool triggerMoving_End = false;
            if (m_offsetPosition != value)
                {
                triggerMoving_End = true;
                TriggerScriptMovingStartEvent();
                }
            StoreUndoState();
            m_offsetPosition = value;

            if (ParentGroup != null && !ParentGroup.IsDeleted)
                {
                ParentGroup.HasGroupChanged = true;
                PhysicsActor actor = PhysActor;
                if (_parentID != 0 && actor != null &&(single || !actor.IsPhysical))
                    {
                    actor.Position = GetWorldPosition();
                    actor.Orientation = GetWorldRotation();

                    // Tell the physics engines that this prim changed.
                    m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPhysicsActorTaint(actor);
                    }
                }
            if (triggerMoving_End)
                TriggerScriptMovingEndEvent();
            }
        

        public void SetGroupPosition(Vector3 value)
            {
            m_groupPosition = new Vector3(value.X, value.Y, value.Z);
            }


        public void FixGroupPosition(Vector3 value, bool single)
            {
            FixGroupPositionComum(true, value, single);
            }

        public void FixGroupPositionComum(bool UpdatePrimActor, Vector3 value, bool single)
        {
            if (ParentGroup != null)
                ParentGroup.HasGroupChanged = true;
            bool TriggerMoving_End = false;
            if (m_groupPosition != value)
            {
                TriggerMoving_End = true;
                TriggerScriptMovingStartEvent();
            }

            m_groupPosition = value;

            PhysicsActor actor = PhysActor;

            if (actor != null)
            {
                if (actor.PhysicsActorType != (int)ActorTypes.Prim)  // for now let other times get updates
                {
                    UpdatePrimActor = true;
                    single = false;
                }
                if (UpdatePrimActor)
                {
                    try
                    {
                        // Root prim actually goes at Position
                        if (_parentID == 0)
                        {
                            actor.Position = value;
                            m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPhysicsActorTaint(actor);
                        }
                        else if (single || !actor.IsPhysical)
                        {
                            // To move the child prim in respect to the group position and rotation we have to calculate
                            actor.Position = GetWorldPosition();
                            actor.Orientation = GetWorldRotation();
                            m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPhysicsActorTaint(actor);
                        }

                        // Tell the physics engines that this prim changed.

                    }
                    catch (Exception e)
                    {
                        m_log.Error("[SCENEOBJECTPART]: GROUP POSITION. " + e.Message);
                    }
                }
            }

            if (m_sitTargetAvatar.Count != 0)
            {
                foreach (UUID avID in m_sitTargetAvatar)
                {
                    if (m_parentGroup != null)
                    {
                        ScenePresence avatar;
                        if (m_parentGroup.Scene.TryGetScenePresence(avID, out avatar))
                        {
                            avatar.ParentPosition = GetWorldPosition();
                        }
                    }
                }
            }
            if (TriggerMoving_End)
                TriggerScriptMovingEndEvent();
        }

        public void ResetExpire()
        {
            Expires = DateTime.Now + new TimeSpan(TimeSpan.TicksPerMinute);
        }

        public bool AddFlag(PrimFlags flag)
        {
            // PrimFlags prevflag = Flags;
            if ((Flags & flag) == 0)
            {
                //m_log.Debug("Adding flag: " + ((PrimFlags) flag).ToString());
                Flags |= flag;

                if (flag == PrimFlags.TemporaryOnRez)
                    ResetExpire();
                return true;
            }
            return false;
            // m_log.Debug("Aprev: " + prevflag.ToString() + " curr: " + Flags.ToString());
        }

        public void AddNewParticleSystem(Primitive.ParticleSystem pSystem)
        {
            ParticleSystem = pSystem.GetBytes();
        }

        public void RemoveParticleSystem()
        {
            ParticleSystem = Utils.EmptyBytes;
        }

        public void AddTextureAnimation(Primitive.TextureAnimation pTexAnim)
        {
            byte[] data = new byte[16];
            int pos = 0;

            // The flags don't like conversion from uint to byte, so we have to do
            // it the crappy way.  See the above function :(

            data[pos] = ConvertScriptUintToByte((uint)pTexAnim.Flags); pos++;
            data[pos] = (byte)pTexAnim.Face; pos++;
            data[pos] = (byte)pTexAnim.SizeX; pos++;
            data[pos] = (byte)pTexAnim.SizeY; pos++;

            Utils.FloatToBytes(pTexAnim.Start).CopyTo(data, pos);
            Utils.FloatToBytes(pTexAnim.Length).CopyTo(data, pos + 4);
            Utils.FloatToBytes(pTexAnim.Rate).CopyTo(data, pos + 8);

            TextureAnimation = data;
        }

        public void AdjustSoundGain(double volume)
        {
            if (volume > 1)
                volume = 1;
            if (volume < 0)
                volume = 0;

            m_parentGroup.Scene.ForEachScenePresence(delegate(ScenePresence sp)
            {
                if (!sp.IsChildAgent)
                    sp.ControllingClient.SendAttachedSoundGainChange(UUID, (float)volume);
            });
        }

        /// <summary>
        /// hook to the physics scene to apply impulse
        /// This is sent up to the group, which then finds the root prim
        /// and applies the force on the root prim of the group
        /// </summary>
        /// <param name="impulsei">Vector force</param>
        /// <param name="localGlobalTF">true for the local frame, false for the global frame</param>
        public void ApplyImpulse(Vector3 impulsei, bool localGlobalTF)
        {
            Vector3 impulse = impulsei;

            if (localGlobalTF)
            {
                Quaternion grot = GetWorldRotation();
                Quaternion AXgrot = grot;
                Vector3 AXimpulsei = impulsei;
                Vector3 newimpulse = AXimpulsei * AXgrot;
                impulse = newimpulse;
            }

            if (m_parentGroup != null)
            {
                m_parentGroup.applyImpulse(impulse);
            }
        }

        /// <summary>
        /// hook to the physics scene to apply angular impulse
        /// This is sent up to the group, which then finds the root prim
        /// and applies the force on the root prim of the group
        /// </summary>
        /// <param name="impulsei">Vector force</param>
        /// <param name="localGlobalTF">true for the local frame, false for the global frame</param>
        public void ApplyAngularImpulse(Vector3 impulsei, bool localGlobalTF)
        {
            Vector3 impulse = impulsei;

            if (localGlobalTF)
            {
                Quaternion grot = GetWorldRotation();
                Quaternion AXgrot = grot;
                Vector3 AXimpulsei = impulsei;
                Vector3 newimpulse = AXimpulsei * AXgrot;
                impulse = newimpulse;
            }

            if (m_parentGroup != null)
            {
                m_parentGroup.applyAngularImpulse(impulse);
            }
        }

        /// <summary>
        /// hook to the physics scene to apply angular impulse
        /// This is sent up to the group, which then finds the root prim
        /// and applies the force on the root prim of the group
        /// </summary>
        /// <param name="impulsei">Vector force</param>
        /// <param name="localGlobalTF">true for the local frame, false for the global frame</param>
        public void SetAngularImpulse(Vector3 impulsei, bool localGlobalTF)
        {
            Vector3 impulse = impulsei;

            if (localGlobalTF)
            {
                Quaternion grot = GetWorldRotation();
                Quaternion AXgrot = grot;
                Vector3 AXimpulsei = impulsei;
                Vector3 newimpulse = AXimpulsei * AXgrot;
                impulse = newimpulse;
            }

            if (m_parentGroup != null)
            {
                m_parentGroup.setAngularImpulse(impulse);
            }
        }

        public Vector3 GetTorque()
        {
            if (m_parentGroup != null)
            {
                m_parentGroup.GetTorque();
            }
            return Vector3.Zero;
        }

        public delegate void AddPhysics();
        public event AddPhysics OnAddPhysics;

        /// <summary>
        /// Apply physics to this part.
        /// </summary>
        /// <param name="rootObjectFlags"></param>
        /// <param name="m_physicalPrim"></param>
        public void ApplyPhysics(uint rootObjectFlags, bool VolumeDetectActive, bool m_physicalPrim)
        {
            bool isPhysical = (((rootObjectFlags & (uint) PrimFlags.Physics) != 0) && m_physicalPrim);
            bool isPhantom = ((rootObjectFlags & (uint) PrimFlags.Phantom) != 0);

            if (IsJoint())
            {
                DoPhysicsPropertyUpdate(isPhysical, true);
            }
            else
            {
                // Special case for VolumeDetection: If VolumeDetection is set, the phantom flag is locally ignored
                if (VolumeDetectActive)
                    isPhantom = false;

                // Added clarification..   since A rigid body is an object that you can kick around, etc.
                bool RigidBody = isPhysical && !isPhantom;

                // The only time the physics scene shouldn't know about the prim is if it's phantom or an attachment, which is phantom by definition
                // or flexible
                if (!isPhantom && !IsAttachment && !(Shape.PathCurve == (byte) Extrusion.Flexible))
                {
  
                    Vector3 tmp = GetWorldPosition();
                    Quaternion qtmp = GetWorldRotation();
                    PhysActor = m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPrimShape(
                        string.Format("{0}/{1}", Name, UUID),
                        Shape,
                        tmp,
                        Scale,
                        qtmp,
                        RigidBody);

                    // Basic Physics returns null..  joy joy joy.
                    if (PhysActor != null)
                    {
                        PhysActor.SOPName = this.Name; // save object name and desc into the PhysActor so ODE internals know the joint/body info
                        PhysActor.SOPDescription = this.Description;
                        PhysActor.LocalID = LocalId;
                        DoPhysicsPropertyUpdate(RigidBody, true);
                        PhysActor.SetVolumeDetect(VolumeDetectActive ? 1 : 0);
                        if(OnAddPhysics != null)
                            OnAddPhysics();
                    }
                    else
                    {
                        //m_log.DebugFormat("[SOP]: physics actor is null for {0} with parent {1}", UUID, this.ParentGroup.UUID);
                    }
                }
            }
        }

        public void ClearUndoState()
        {
            lock (m_undo)
            {
                m_undo = new UndoStack<UndoState>(5);
            }
            lock (m_redo)
            {
                m_redo = new UndoStack<UndoState>(5);
            }
            StoreUndoState();
        }

        public byte ConvertScriptUintToByte(uint indata)
        {
            byte outdata = (byte)TextureAnimFlags.NONE;
            if ((indata & 1) != 0) outdata |= (byte)TextureAnimFlags.ANIM_ON;
            if ((indata & 2) != 0) outdata |= (byte)TextureAnimFlags.LOOP;
            if ((indata & 4) != 0) outdata |= (byte)TextureAnimFlags.REVERSE;
            if ((indata & 8) != 0) outdata |= (byte)TextureAnimFlags.PING_PONG;
            if ((indata & 16) != 0) outdata |= (byte)TextureAnimFlags.SMOOTH;
            if ((indata & 32) != 0) outdata |= (byte)TextureAnimFlags.ROTATE;
            if ((indata & 64) != 0) outdata |= (byte)TextureAnimFlags.SCALE;
            return outdata;
        }

        /// <summary>
        /// Duplicates this part.
        /// </summary>
        /// <param name="localID"></param>
        /// <param name="AgentID"></param>
        /// <param name="GroupID"></param>
        /// <param name="linkNum"></param>
        /// <param name="userExposed">True if the duplicate will immediately be in the scene, false otherwise</param>
        /// <returns></returns>
        /// 
        /* not in use
                public SceneObjectPart Copy(uint localID, UUID AgentID, UUID GroupID, int linkNum, bool userExposed, bool ChangeScripts, SceneObjectGroup parent)
                {
                    SceneObjectPart dupe = (SceneObjectPart)MemberwiseClone();
                    dupe.m_parentGroup = parent;
                    dupe.m_shape = m_shape.Copy();
                    dupe.m_regionHandle = m_regionHandle;

                    if (userExposed)
                        {
                        //                dupe.UUID = UUID.Random();  can't whould mess original inventory
                        dupe.m_uuid = UUID.Random();
                        dupe.ParentGroup.HasGroupChanged = true;
                        }

                    //memberwiseclone means it also clones the physics actor reference
                    // This will make physical prim 'bounce' if not set to null.
                    if (!userExposed)
                        dupe.PhysActor = null;

                    dupe._ownerID = AgentID;
                    dupe._groupID = GroupID;
                    dupe.m_groupPosition = m_groupPosition;
                    dupe.m_offsetPosition = m_offsetPosition;
                    dupe.m_rotationOffset = m_rotationOffset;
                    dupe.Velocity = new Vector3(0, 0, 0);
                    dupe.Acceleration = new Vector3(0, 0, 0);
                    dupe.AngularVelocity = new Vector3(0, 0, 0);
                    dupe.Flags = Flags;

                    dupe._ownershipCost = _ownershipCost;
                    dupe._objectSaleType = _objectSaleType;
                    dupe._salePrice = _salePrice;
                    dupe._category = _category;
                    dupe.Rezzed = Rezzed;

                    dupe.m_inventory = new SceneObjectPartInventory(dupe);
                    dupe.m_inventory.Items = (TaskInventoryDictionary)m_inventory.Items.Clone();

                    if (userExposed)
                    {
                        dupe.ResetEntityIDs();
                        dupe.LinkNum = linkNum;
        //              dupe.CloneScrips(this);  // go to fix our scripts
                        dupe.m_inventory.HasInventoryChanged = true;

                    }
                    else
                    {
                        dupe.m_inventory.HasInventoryChanged = m_inventory.HasInventoryChanged;
                    }

                    // Move afterwards ResetIDs as it clears the localID
                    dupe.LocalId = localID;
                    // This may be wrong...    it might have to be applied in SceneObjectGroup to the object that's being duplicated.
                    dupe._lastOwnerID = OwnerID;

                    byte[] extraP = new byte[Shape.ExtraParams.Length];
                    Array.Copy(Shape.ExtraParams, extraP, extraP.Length);
                    dupe.Shape.ExtraParams = extraP;

                    if (userExposed)
                    {
                        if (dupe.m_shape.SculptEntry && dupe.m_shape.SculptTexture != UUID.Zero)
                        {
                            m_parentGroup.Scene.AssetService.Get(dupe.m_shape.SculptTexture.ToString(), dupe, AssetReceived); 
                        }

                        PrimitiveBaseShape pbs = dupe.Shape;
                        if (dupe.PhysActor != null)
                            {
                            dupe.PhysActor.LocalID = localID;
                            dupe.PhysActor = ParentGroup.Scene.PhysicsScene.AddPrimShape(
                                dupe.Name,
                                pbs,
                                dupe.AbsolutePosition,
                                dupe.Scale,
                                dupe.RotationOffset,
                                dupe.PhysActor.IsPhysical);

                            dupe.PhysActor.LocalID = dupe.LocalId;
                            dupe.DoPhysicsPropertyUpdate(dupe.PhysActor.IsPhysical, true);

                            if (VolumeDetectActive)
                                dupe.PhysActor.SetVolumeDetect(1);
                        }
                    }

                    ParentGroup.Scene.EventManager.TriggerOnSceneObjectPartCopy(dupe, this);

        //            m_log.DebugFormat("[SCENE OBJECT PART]: Clone of {0} {1} finished", Name, UUID);

                    return dupe;
                }
        */
        public SceneObjectPart Copy(SceneObjectGroup parent,bool clonePhys)
        {
            SceneObjectPart dupe = (SceneObjectPart)MemberwiseClone();
            dupe.m_parentGroup = parent;
            dupe.m_shape = m_shape.Copy();
            dupe.m_regionHandle = m_regionHandle;

            //memberwiseclone means it also clones the physics actor reference
            // This will make physical prim 'bounce' if not set to null.

            if(!clonePhys)
                dupe.PhysActor = null;

            dupe._groupID = GroupID;
            dupe.m_groupPosition = m_groupPosition;
            dupe.m_offsetPosition = m_offsetPosition;
            dupe.m_rotationOffset = m_rotationOffset;
            dupe.Velocity = new Vector3(0, 0, 0);
            dupe.Acceleration = new Vector3(0, 0, 0);
            dupe.AngularVelocity = new Vector3(0, 0, 0);
            dupe.Flags = Flags;
            dupe.LinkNum = LinkNum;
            dupe.SitTargetAvatar = ParentGroup.RootPart.SitTargetAvatar;

            dupe._ownershipCost = _ownershipCost;
            dupe._objectSaleType = _objectSaleType;
            dupe._salePrice = _salePrice;
            dupe._category = _category;
            dupe.Rezzed = Rezzed;

            dupe.m_inventory = new SceneObjectPartInventory(dupe);
            dupe.m_inventory.Items = (TaskInventoryDictionary)m_inventory.Items.Clone();
            dupe.m_inventory.HasInventoryChanged = m_inventory.HasInventoryChanged;

            byte[] extraP = new byte[Shape.ExtraParams.Length];
            Array.Copy(Shape.ExtraParams, extraP, extraP.Length);
            dupe.Shape.ExtraParams = extraP;

            dupe.m_scriptEvents = new Dictionary<UUID,scriptEvents>();
            if (dupe.m_shape.SculptEntry && dupe.m_shape.SculptTexture != UUID.Zero)
            {
                m_parentGroup.Scene.AssetService.Get(dupe.m_shape.SculptTexture.ToString(), dupe, AssetReceived);
            }

            /*PrimitiveBaseShape pbs = dupe.Shape;
            if (dupe.PhysActor != null)
            {
                dupe.PhysActor = ParentGroup.Scene.PhysicsScene.AddPrimShape(
                    dupe.Name,
                    pbs,
                    dupe.AbsolutePosition,
                    dupe.Scale,
                    dupe.RotationOffset,
                    dupe.PhysActor.IsPhysical);

                dupe.PhysActor.LocalID = dupe.LocalId;
                dupe.DoPhysicsPropertyUpdate(dupe.PhysActor.IsPhysical, true);

                if (VolumeDetectActive)
                    dupe.PhysActor.SetVolumeDetect(1);
            }*/

            return dupe;
        }

        protected void AssetReceived(string id, Object sender, AssetBase asset)
        {
            if (asset != null)
            {
                SceneObjectPart sop = (SceneObjectPart)sender;
                if (sop != null)
                    sop.SculptTextureCallback(asset.FullID, asset);
            }
        }

        public delegate void RemovePhysics();
        public event RemovePhysics OnRemovePhysics;

        public void DoPhysicsPropertyUpdate(bool UsePhysics, bool isNew)
        {
            if (IsJoint())
            {
                if (UsePhysics)
                {
                    INinjaPhysicsModule ninjaMod = ParentGroup.Scene.RequestModuleInterface<INinjaPhysicsModule>();
                    if(ninjaMod != null)
                        ninjaMod.jointCreate(this);
                }
                else
                {
                    if (isNew)
                    {
                        // if the joint proxy is new, and it is not physical, do nothing. There is no joint in ODE to
                        // delete, and if we try to delete it, due to asynchronous processing, the deletion request
                        // will get processed later at an indeterminate time, which could cancel a later-arriving
                        // joint creation request.
                    }
                    else
                    {
                        // here we turn off the joint object, so remove the joint from the physics scene
                        m_parentGroup.Scene.SceneGraph.PhysicsScene.RequestJointDeletion(Name); // FIXME: what if the name changed?

                        // make sure client isn't interpolating the joint proxy object
                        Velocity = Vector3.Zero;
                        AngularVelocity = Vector3.Zero;
                        Acceleration = Vector3.Zero;
                    }
                }
            }
            else
            {
                if (PhysActor != null)
                {
                    if (UsePhysics != PhysActor.IsPhysical || isNew)
                    {
                        if (PhysActor.IsPhysical) // implies UsePhysics==false for this block
                        {
                            PhysActor.OnRequestTerseUpdate -= PhysicsRequestingTerseUpdate;
                            PhysActor.OnSignificantMovement -= ParentGroup.CheckForSignificantMovement;
                            PhysActor.OnOutOfBounds -= PhysicsOutOfBounds;
                            PhysActor.delink();

                            if (ParentGroup.Scene.SceneGraph.PhysicsScene.SupportsNINJAJoints && (!isNew))
                            {
                                // destroy all joints connected to this now deactivated body
                                m_parentGroup.Scene.SceneGraph.PhysicsScene.RemoveAllJointsConnectedToActorThreadLocked(PhysActor);
                            }

                            if(OnRemovePhysics != null)
                                OnRemovePhysics();

                            // stop client-side interpolation of all joint proxy objects that have just been deleted
                            // this is done because RemoveAllJointsConnectedToActor invokes the OnJointDeactivated callback,
                            // which stops client-side interpolation of deactivated joint proxy objects.
                        }

                        if (!UsePhysics && !isNew)
                        {
                            // reset velocity to 0 on physics switch-off. Without that, the client thinks the
                            // prim still has velocity and continues to interpolate its position along the old
                            // velocity-vector.
                            Velocity = new Vector3(0, 0, 0);
                            Acceleration = new Vector3(0, 0, 0);
                            AngularVelocity = new Vector3(0, 0, 0);
                            //RotationalVelocity = new Vector3(0, 0, 0);
                        }

                        PhysActor.IsPhysical = UsePhysics;


                        // If we're not what we're supposed to be in the physics scene, recreate ourselves.
                        //m_parentGroup.Scene.PhysicsScene.RemovePrim(PhysActor);
                        /// that's not wholesome.  Had to make Scene public
                        //PhysActor = null;

                        if ((Flags & PrimFlags.Phantom) == 0)
                        {
                            if (UsePhysics)
                            {
                                PhysActor.OnRequestTerseUpdate += PhysicsRequestingTerseUpdate;
                                PhysActor.OnSignificantMovement += ParentGroup.CheckForSignificantMovement;
                                PhysActor.OnOutOfBounds += PhysicsOutOfBounds;
                                if (_parentID != 0 && _parentID != LocalId)
                                {
                                    if (ParentGroup.RootPart.PhysActor != null)
                                    {
                                        PhysActor.link(ParentGroup.RootPart.PhysActor);
                                    }
                                }
                            }
                        }
                    }
                    m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPhysicsActorTaint(PhysActor);
                }
            }
            ParentGroup.Scene.AuroraEventManager.FireGenericEventHandler("ObjectChangedPhysicalStatus", this.ParentGroup);
        }

        public List<UUID> GetAvatarOnSitTarget()
        {
            return m_sitTargetAvatar;
        }

        public bool GetDieAtEdge()
        {
            if (m_parentGroup == null)
                return false;
            if (m_parentGroup.IsDeleted)
                return false;

            return m_parentGroup.RootPart.DIE_AT_EDGE;
        }

        public bool GetReturnAtEdge()
        {
            if (m_parentGroup == null)
                return false;
            if (m_parentGroup.IsDeleted)
                return false;

            return m_parentGroup.RootPart.RETURN_AT_EDGE;
        }

        public void SetReturnAtEdge(bool p)
        {
            if (m_parentGroup == null)
                return;
            if (m_parentGroup.IsDeleted)
                return;

            m_parentGroup.RootPart.RETURN_AT_EDGE = p;
        }

        public bool GetBlockGrab()
        {
            if (m_parentGroup == null)
                return false;
            if (m_parentGroup.IsDeleted)
                return false;

            return m_parentGroup.RootPart.BlockGrab;
        }

        public void SetBlockGrab(bool p)
        {
            if (m_parentGroup == null)
                return;
            if (m_parentGroup.IsDeleted)
                return;

            m_parentGroup.RootPart.BlockGrab = p;
        }

        public void SetStatusSandbox(bool p)
        {
            if (m_parentGroup == null)
                return;
            if (m_parentGroup.IsDeleted)
                return;
            StatusSandboxPos = m_parentGroup.RootPart.AbsolutePosition;
            m_parentGroup.RootPart.StatusSandbox = p;
        }

        public bool GetStatusSandbox()
        {
            if (m_parentGroup == null)
                return false;
            if (m_parentGroup.IsDeleted)
                return false;

            return m_parentGroup.RootPart.StatusSandbox;
        }

        public int GetAxisRotation(int axis)
        {
            //Cannot use ScriptBaseClass constants as no referance to it currently.
            if (axis == 2)//STATUS_ROTATE_X
                return STATUS_ROTATE_X;
            if (axis == 4)//STATUS_ROTATE_Y
                return STATUS_ROTATE_Y;
            if (axis == 8)//STATUS_ROTATE_Z
                return STATUS_ROTATE_Z;

            return 0;
        }

        public double GetDistanceTo(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public uint GetEffectiveObjectFlags()
        {
            // Commenting this section of code out since it doesn't actually do anything, as enums are handled by 
            // value rather than reference
//            PrimFlags f = _flags;
//            if (m_parentGroup == null || m_parentGroup.RootPart == this)
//                f &= ~(PrimFlags.Touch | PrimFlags.Money);

            return (uint)Flags | (uint)LocalFlags;
        }

        public Vector3 GetGeometricCenter()
        {
            if (PhysActor != null)
            {
                return new Vector3(PhysActor.CenterOfMass.X, PhysActor.CenterOfMass.Y, PhysActor.CenterOfMass.Z);
            }
            else
            {
                return new Vector3(0, 0, 0);
            }
        }

        public float GetMass()
        {
            if (ParentGroup.RootPart.UUID == UUID)
            {
                if (PhysActor != null)
                {
                    return PhysActor.Mass;
                }
                else
                {
                    return 0;
                }
            }
            else
            {
                return ParentGroup.RootPart.GetMass();
            }
        }

        public Vector3 GetForce()
        {
            if (PhysActor != null)
                return PhysActor.Force;
            else
                return Vector3.Zero;
        }

        public void GetProperties(IClientAPI client)
        {
            client.SendObjectPropertiesReply(new List<ISceneEntity>(new ISceneEntity []{ this }));
        }

        public UUID GetRootPartUUID()
        {
            if (m_parentGroup != null)
            {
                return m_parentGroup.UUID;
            }
            return UUID.Zero;
        }

        /// <summary>
        /// Method for a prim to get it's world position from the group.
        /// Remember, the Group Position simply gives the position of the group itself
        /// </summary>
        /// <returns>A Linked Child Prim objects position in world</returns>
        public Vector3 GetWorldPosition()
        {
            Quaternion parentRot = ParentGroup.RootPart.RotationOffset;
            return (GroupPosition + OffsetPosition * parentRot);
        }

        /// <summary>
        /// Gets the rotation of this prim offset by the group rotation
        /// </summary>
        /// <returns></returns>
        public Quaternion GetWorldRotation()
        {
            Quaternion newRot = RotationOffset;

            if (_parentID !=0)
            {
                Quaternion parentRot = ParentGroup.RootPart.RotationOffset;
                newRot = parentRot * newRot;
            }

            return newRot;
        }

        public void MoveToTarget(Vector3 target, float tau)
        {
            if (tau > 0)
            {
                m_parentGroup.moveToTarget(target, tau);
            }
            else
            {
                StopMoveToTarget();
            }
        }

        public void SetMoveToTarget(bool Enabled, Vector3 target, float tau)
        {
            if (Enabled)
            {
                m_initialPIDLocation = AbsolutePosition;
                PIDTarget = target;
                PIDTau = tau;
                PIDActive = true;
            }
            else
            {
                PIDActive = false;
                m_initialPIDLocation = Vector3.Zero;
            }
        }

        /// <summary>
        /// Uses a PID to attempt to clamp the object on the Z axis at the given height over tau seconds.
        /// </summary>
        /// <param name="height">Height to hover.  Height of zero disables hover.</param>
        /// <param name="hoverType">Determines what the height is relative to </param>
        /// <param name="tau">Number of seconds over which to reach target</param>
        public void SetHoverHeight(float height, PIDHoverType hoverType, float tau)
        {
            m_parentGroup.SetHoverHeight(height, hoverType, tau);
        }

        public void StopHover()
        {
            m_parentGroup.SetHoverHeight(0f, PIDHoverType.Ground, 0f);
        }

        public virtual void OnGrab(Vector3 offsetPos, IClientAPI remoteClient)
        {
        }

        public void PhysicsCollision(EventArgs e)
        {
            // single threaded here
            if (e == null)
            {
                return;
            }

            CollisionEventUpdate a = (CollisionEventUpdate)e;
            Dictionary<uint, ContactPoint> collissionswith = a.m_objCollisionList;
            List<uint> thisHitColliders = new List<uint>();
            List<uint> endedColliders = new List<uint>();
            List<uint> startedColliders = new List<uint>();

            // calculate things that started colliding this time
            // and build up list of colliders this time
            foreach (uint localID in collissionswith.Keys)
            {
                thisHitColliders.Add(localID);
                if (!m_lastColliders.Contains(localID))
                {
                    startedColliders.Add(localID);
                }
                //m_log.Debug("[OBJECT]: Collided with:" + localid.ToString() + " at depth of: " + collissionswith[localid].ToString());
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
            m_lastColliders.AddRange(startedColliders);
            // remove things that ended colliding from the last colliders list
            foreach (uint localID in endedColliders)
            {
                m_lastColliders.Remove(localID);
            }

            if (m_parentGroup == null)
                return;
            if (m_parentGroup.IsDeleted)
                return;

            const string SoundGlassCollision = "6a45ba0b-5775-4ea8-8513-26008a17f873";
            const string SoundMetalCollision = "9e5c1297-6eed-40c0-825a-d9bcd86e3193";
            const string SoundStoneCollision = "9538f37c-456e-4047-81be-6435045608d4";
            const string SoundFleshCollision = "dce5fdd4-afe4-4ea1-822f-dd52cac46b08";
            const string SoundPlasticCollision = "0e24a717-b97e-4b77-9c94-b59a5a88b2da";
            const string SoundRubberCollision = "153c8bf7-fb89-4d89-b263-47e58b1b4774";
            const string SoundWoodCollision = "063c97d3-033a-4e9b-98d8-05c8074922cb";
            
            // play the sound.
            if (startedColliders.Count > 0 && CollisionSound != UUID.Zero && CollisionSoundVolume > 0.0f)
            {
                SendSound(CollisionSound.ToString(), CollisionSoundVolume, true, (byte)0, 0, false, false);
            }
            else if (startedColliders.Count > 0)
            {
                switch (a.collidertype)
                {
                    case (int)ActorTypes.Agent:
                        break; // Agents will play the sound so we don't

                    case (int)ActorTypes.Ground:
                        if (collissionswith[startedColliders[0]].PenetrationDepth < 0.17)
                            SendSound(SoundWoodCollision, 1, true, 0, 0, false, false);
                        else
                            SendSound(Sounds.OBJECT_COLLISION.ToString(), 1, true, 0, 0, false, false);
                        break; //Always play the click or thump sound when hitting ground

                    case (int)ActorTypes.Prim:
                        if (m_material == OpenMetaverse.Material.Flesh)
                            SendSound(SoundFleshCollision.ToString(), 1, true, 0, 0, false, false);
                        else if (m_material == OpenMetaverse.Material.Glass)
                            SendSound(SoundGlassCollision, 1, true, 0, 0, false, false);
                        else if (m_material == OpenMetaverse.Material.Metal)
                            SendSound(SoundMetalCollision, 1, true, 0, 0, false, false);
                        else if (m_material == OpenMetaverse.Material.Plastic)
                            SendSound(SoundPlasticCollision, 1, true, 0, 0, false, false);
                        else if (m_material == OpenMetaverse.Material.Rubber)
                            SendSound(SoundRubberCollision, 1, true, 0, 0, false, false);
                        else if (m_material == OpenMetaverse.Material.Stone)
                            SendSound(SoundStoneCollision, 1, true, 0, 0, false, false);
                        else if (m_material == OpenMetaverse.Material.Wood)
                            SendSound(SoundWoodCollision, 1, true, 0, 0, false, false);
                        break; //Play based on material type in prim2prim collisions

                    default:
                        break; //Unclear of what this object is, no sounds
                }
            }
            if (CollisionSprite != UUID.Zero && CollisionSoundVolume > 0.0f) // The collision volume isn't a mistake, its an SL feature/bug
            {
                // TODO: make a sprite!

            }
            if (((AggregateScriptEvents & scriptEvents.collision) != 0) ||
               ((AggregateScriptEvents & scriptEvents.collision_end) != 0) ||
               ((AggregateScriptEvents & scriptEvents.collision_start) != 0) ||
               ((AggregateScriptEvents & scriptEvents.land_collision_start) != 0) ||
               ((AggregateScriptEvents & scriptEvents.land_collision) != 0) ||
               ((AggregateScriptEvents & scriptEvents.land_collision_end) != 0) ||
               (CollisionSound != UUID.Zero) ||
                PassCollisions != 2)
            {

                if ((m_parentGroup.RootPart.ScriptEvents & scriptEvents.collision_start) != 0)
                {
                    // do event notification
                    if (startedColliders.Count > 0)
                    {
                        ColliderArgs StartCollidingMessage = new ColliderArgs();
                        List<DetectedObject> colliding = new List<DetectedObject>();
                        foreach (uint localId in startedColliders)
                        {
                            if (localId == 0)
                                continue;
                            // always running this check because if the user deletes the object it would return a null reference.
                            if (m_parentGroup == null)
                                return;

                            if (m_parentGroup.Scene == null)
                                return;

                            SceneObjectPart obj = m_parentGroup.Scene.GetSceneObjectPart(localId);
                            string data = "";
                            if (obj != null)
                            {
                                if (m_parentGroup.RootPart.CollisionFilter.ContainsValue(obj.UUID.ToString()) || m_parentGroup.RootPart.CollisionFilter.ContainsValue(obj.Name))
                                {
                                    bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                    //If it is 1, it is to accept ONLY collisions from this object
                                    if (found)
                                    {
                                        DetectedObject detobj = new DetectedObject();
                                        detobj.keyUUID = obj.UUID;
                                        detobj.nameStr = obj.Name;
                                        detobj.ownerUUID = obj._ownerID;
                                        detobj.posVector = obj.AbsolutePosition;
                                        detobj.rotQuat = obj.GetWorldRotation();
                                        detobj.velVector = obj.Velocity;
                                        detobj.colliderType = 0;
                                        detobj.groupUUID = obj._groupID;
                                        colliding.Add(detobj);
                                    }
                                    //If it is 0, it is to not accept collisions from this object
                                    else
                                    {
                                    }
                                }
                                else
                                {
                                    bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                    //If it is 1, it is to accept ONLY collisions from this object, so this other object will not work
                                    if (!found)
                                    {
                                        DetectedObject detobj = new DetectedObject();
                                        detobj.keyUUID = obj.UUID;
                                        detobj.nameStr = obj.Name;
                                        detobj.ownerUUID = obj._ownerID;
                                        detobj.posVector = obj.AbsolutePosition;
                                        detobj.rotQuat = obj.GetWorldRotation();
                                        detobj.velVector = obj.Velocity;
                                        detobj.colliderType = 0;
                                        detobj.groupUUID = obj._groupID;
                                        colliding.Add(detobj);
                                    }
                                }
                            }
                            else
                            {
                                ScenePresence av = ParentGroup.Scene.SceneGraph.GetScenePresence(localId);
                                if(av != null)
                                {
                                    if (av.LocalId == localId)
                                    {
                                        if (m_parentGroup.RootPart.CollisionFilter.ContainsValue(av.UUID.ToString()) || m_parentGroup.RootPart.CollisionFilter.ContainsValue(av.Name))
                                        {
                                            bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                            //If it is 1, it is to accept ONLY collisions from this avatar
                                            if (found)
                                            {
                                                DetectedObject detobj = new DetectedObject();
                                                detobj.keyUUID = av.UUID;
                                                detobj.nameStr = av.ControllingClient.Name;
                                                detobj.ownerUUID = av.UUID;
                                                detobj.posVector = av.AbsolutePosition;
                                                detobj.rotQuat = av.Rotation;
                                                detobj.velVector = av.Velocity;
                                                detobj.colliderType = 0;
                                                detobj.groupUUID = av.ControllingClient.ActiveGroupId;
                                                colliding.Add(detobj);
                                            }
                                            //If it is 0, it is to not accept collisions from this avatar
                                            else
                                            {
                                            }
                                        }
                                        else
                                        {
                                            bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                            //If it is 1, it is to accept ONLY collisions from this avatar, so this other avatar will not work
                                            if (!found)
                                            {
                                                DetectedObject detobj = new DetectedObject();
                                                detobj.keyUUID = av.UUID;
                                                detobj.nameStr = av.ControllingClient.Name;
                                                detobj.ownerUUID = av.UUID;
                                                detobj.posVector = av.AbsolutePosition;
                                                detobj.rotQuat = av.Rotation;
                                                detobj.velVector = av.Velocity;
                                                detobj.colliderType = 0;
                                                detobj.groupUUID = av.ControllingClient.ActiveGroupId;
                                                colliding.Add(detobj);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        if (colliding.Count > 0)
                        {
                            StartCollidingMessage.Colliders = colliding;
                            // always running this check because if the user deletes the object it would return a null reference.
                            if (m_parentGroup == null)
                                return;

                            if (m_parentGroup.Scene == null)
                                return;
                            //Always send to the prim it is occuring to
                            m_parentGroup.Scene.EventManager.TriggerScriptCollidingStart(this, StartCollidingMessage);
                            if ((this.UUID != this.ParentGroup.RootPart.UUID))
                            {
                                const int PASS_IF_NOT_HANDLED = 0;
                                const int PASS_ALWAYS = 1;
                                const int PASS_NEVER = 2;
                                if (this.PassCollisions == PASS_NEVER)
                                {
                                }
                                if (this.PassCollisions == PASS_ALWAYS)
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptCollidingStart(this.ParentGroup.RootPart, StartCollidingMessage);
                                }
                                else if (((this.ScriptEvents & scriptEvents.collision_start) == 0) && this.PassCollisions == PASS_IF_NOT_HANDLED) //If no event in this prim, pass to parent
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptCollidingStart(this.ParentGroup.RootPart, StartCollidingMessage);
                                }
                            }
                        }
                    }
                }

                if ((m_parentGroup.RootPart.ScriptEvents & scriptEvents.collision) != 0)
                {
                    if (m_lastColliders.Count > 0)
                    {
                        ColliderArgs CollidingMessage = new ColliderArgs();
                        List<DetectedObject> colliding = new List<DetectedObject>();
                        foreach (uint localId in m_lastColliders)
                        {
                            // always running this check because if the user deletes the object it would return a null reference.
                            if (localId == 0)
                                continue;

                            if (m_parentGroup == null)
                                return;

                            if (m_parentGroup.Scene == null)
                                return;

                            SceneObjectPart obj = m_parentGroup.Scene.GetSceneObjectPart(localId);
                            string data = "";
                            if (obj != null)
                            {
                                if (m_parentGroup.RootPart.CollisionFilter.ContainsValue(obj.UUID.ToString()) || m_parentGroup.RootPart.CollisionFilter.ContainsValue(obj.Name))
                                {
                                    bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                    //If it is 1, it is to accept ONLY collisions from this object
                                    if (found)
                                    {
                                        DetectedObject detobj = new DetectedObject();
                                        detobj.keyUUID = obj.UUID;
                                        detobj.nameStr = obj.Name;
                                        detobj.ownerUUID = obj._ownerID;
                                        detobj.posVector = obj.AbsolutePosition;
                                        detobj.rotQuat = obj.GetWorldRotation();
                                        detobj.velVector = obj.Velocity;
                                        detobj.colliderType = 0;
                                        detobj.groupUUID = obj._groupID;
                                        colliding.Add(detobj);
                                    }
                                    //If it is 0, it is to not accept collisions from this object
                                    else
                                    {
                                    }
                                }
                                else
                                {
                                    bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                    //If it is 1, it is to accept ONLY collisions from this object, so this other object will not work
                                    if (!found)
                                    {
                                        DetectedObject detobj = new DetectedObject();
                                        detobj.keyUUID = obj.UUID;
                                        detobj.nameStr = obj.Name;
                                        detobj.ownerUUID = obj._ownerID;
                                        detobj.posVector = obj.AbsolutePosition;
                                        detobj.rotQuat = obj.GetWorldRotation();
                                        detobj.velVector = obj.Velocity;
                                        detobj.colliderType = 0;
                                        detobj.groupUUID = obj._groupID;
                                        colliding.Add(detobj);
                                    }
                                }
                            }
                            else
                            {
                                ScenePresence av = ParentGroup.Scene.SceneGraph.GetScenePresence(localId);
                                if (av.LocalId == localId)
                                {
                                    if (m_parentGroup.RootPart.CollisionFilter.ContainsValue(av.UUID.ToString()) || m_parentGroup.RootPart.CollisionFilter.ContainsValue(av.Name))
                                    {
                                        bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                        //If it is 1, it is to accept ONLY collisions from this avatar
                                        if (found)
                                        {
                                            DetectedObject detobj = new DetectedObject();
                                            detobj.keyUUID = av.UUID;
                                            detobj.nameStr = av.ControllingClient.Name;
                                            detobj.ownerUUID = av.UUID;
                                            detobj.posVector = av.AbsolutePosition;
                                            detobj.rotQuat = av.Rotation;
                                            detobj.velVector = av.Velocity;
                                            detobj.colliderType = 0;
                                            detobj.groupUUID = av.ControllingClient.ActiveGroupId;
                                            colliding.Add(detobj);
                                        }
                                        //If it is 0, it is to not accept collisions from this avatar
                                        else
                                        {
                                        }
                                    }
                                    else
                                    {
                                        bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                        //If it is 1, it is to accept ONLY collisions from this avatar, so this other avatar will not work
                                        if (!found)
                                        {
                                            DetectedObject detobj = new DetectedObject();
                                            detobj.keyUUID = av.UUID;
                                            detobj.nameStr = av.ControllingClient.Name;
                                            detobj.ownerUUID = av.UUID;
                                            detobj.posVector = av.AbsolutePosition;
                                            detobj.rotQuat = av.Rotation;
                                            detobj.velVector = av.Velocity;
                                            detobj.colliderType = 0;
                                            detobj.groupUUID = av.ControllingClient.ActiveGroupId;
                                            colliding.Add(detobj);
                                        }
                                    }

                                }
                            }
                        }
                        if (colliding.Count > 0)
                        {
                            CollidingMessage.Colliders = colliding;
                            // always running this check because if the user deletes the object it would return a null reference.
                            if (m_parentGroup == null)
                                return;

                            if (m_parentGroup.Scene == null)
                                return;

                            m_parentGroup.Scene.EventManager.TriggerScriptColliding(this, CollidingMessage);
                            
                            if ((this.UUID != this.ParentGroup.RootPart.UUID))
                            {
                                const int PASS_IF_NOT_HANDLED = 0;
                                const int PASS_ALWAYS = 1;
                                const int PASS_NEVER = 2;
                                if (this.PassCollisions == PASS_NEVER)
                                {
                                }
                                if (this.PassCollisions == PASS_ALWAYS)
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptColliding(this.ParentGroup.RootPart, CollidingMessage);
                                }
                                else if (((this.ScriptEvents & scriptEvents.collision) == 0) && this.PassCollisions == PASS_IF_NOT_HANDLED) //If no event in this prim, pass to parent
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptColliding(this.ParentGroup.RootPart, CollidingMessage);
                                }
                            }
                        }
                    }
                }

                if ((m_parentGroup.RootPart.ScriptEvents & scriptEvents.collision_end) != 0)
                {
                    if (endedColliders.Count > 0)
                    {
                        ColliderArgs EndCollidingMessage = new ColliderArgs();
                        List<DetectedObject> colliding = new List<DetectedObject>();
                        foreach (uint localId in endedColliders)
                        {
                            if (localId == 0)
                                continue;

                            // always running this check because if the user deletes the object it would return a null reference.
                            if (m_parentGroup == null)
                                return;
                            if (m_parentGroup.Scene == null)
                                return;
                            SceneObjectPart obj = m_parentGroup.Scene.GetSceneObjectPart(localId);
                            string data = "";
                            if (obj != null)
                            {
                                if (m_parentGroup.RootPart.CollisionFilter.ContainsValue(obj.UUID.ToString()) || m_parentGroup.RootPart.CollisionFilter.ContainsValue(obj.Name))
                                {
                                    bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                    //If it is 1, it is to accept ONLY collisions from this object
                                    if (found)
                                    {
                                        DetectedObject detobj = new DetectedObject();
                                        detobj.keyUUID = obj.UUID;
                                        detobj.nameStr = obj.Name;
                                        detobj.ownerUUID = obj._ownerID;
                                        detobj.posVector = obj.AbsolutePosition;
                                        detobj.rotQuat = obj.GetWorldRotation();
                                        detobj.velVector = obj.Velocity;
                                        detobj.colliderType = 0;
                                        detobj.groupUUID = obj._groupID;
                                        colliding.Add(detobj);
                                    }
                                    //If it is 0, it is to not accept collisions from this object
                                    else
                                    {
                                    }
                                }
                                else
                                {
                                    bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                    //If it is 1, it is to accept ONLY collisions from this object, so this other object will not work
                                    if (!found)
                                    {
                                        DetectedObject detobj = new DetectedObject();
                                        detobj.keyUUID = obj.UUID;
                                        detobj.nameStr = obj.Name;
                                        detobj.ownerUUID = obj._ownerID;
                                        detobj.posVector = obj.AbsolutePosition;
                                        detobj.rotQuat = obj.GetWorldRotation();
                                        detobj.velVector = obj.Velocity;
                                        detobj.colliderType = 0;
                                        detobj.groupUUID = obj._groupID;
                                        colliding.Add(detobj);
                                    }
                                }
                            }
                            else
                            {
                                ScenePresence av = ParentGroup.Scene.SceneGraph.GetScenePresence(localId);
                                if(av != null)
                                {
                                    if (av.LocalId == localId)
                                    {
                                        if (m_parentGroup.RootPart.CollisionFilter.ContainsValue(av.UUID.ToString()) || m_parentGroup.RootPart.CollisionFilter.ContainsValue(av.Name))
                                        {
                                            bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                            //If it is 1, it is to accept ONLY collisions from this avatar
                                            if (found)
                                            {
                                                DetectedObject detobj = new DetectedObject();
                                                detobj.keyUUID = av.UUID;
                                                detobj.nameStr = av.ControllingClient.Name;
                                                detobj.ownerUUID = av.UUID;
                                                detobj.posVector = av.AbsolutePosition;
                                                detobj.rotQuat = av.Rotation;
                                                detobj.velVector = av.Velocity;
                                                detobj.colliderType = 0;
                                                detobj.groupUUID = av.ControllingClient.ActiveGroupId;
                                                colliding.Add(detobj);
                                            }
                                            //If it is 0, it is to not accept collisions from this avatar
                                            else
                                            {
                                            }
                                        }
                                        else
                                        {
                                            bool found = m_parentGroup.RootPart.CollisionFilter.TryGetValue(1, out data);
                                            //If it is 1, it is to accept ONLY collisions from this avatar, so this other avatar will not work
                                            if (!found)
                                            {
                                                DetectedObject detobj = new DetectedObject();
                                                detobj.keyUUID = av.UUID;
                                                detobj.nameStr = av.ControllingClient.Name;
                                                detobj.ownerUUID = av.UUID;
                                                detobj.posVector = av.AbsolutePosition;
                                                detobj.rotQuat = av.Rotation;
                                                detobj.velVector = av.Velocity;
                                                detobj.colliderType = 0;
                                                detobj.groupUUID = av.ControllingClient.ActiveGroupId;
                                                colliding.Add(detobj);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (colliding.Count > 0)
                        {
                            EndCollidingMessage.Colliders = colliding;
                            // always running this check because if the user deletes the object it would return a null reference.
                            if (m_parentGroup == null)
                                return;

                            if (m_parentGroup.Scene == null)
                                return;

                            m_parentGroup.Scene.EventManager.TriggerScriptCollidingEnd(this, EndCollidingMessage);

                            if ((this.UUID != this.ParentGroup.RootPart.UUID))
                            {
                                const int PASS_IF_NOT_HANDLED = 0;
                                const int PASS_ALWAYS = 1;
                                const int PASS_NEVER = 2;
                                if (this.PassCollisions == PASS_NEVER)
                                {
                                }
                                if (this.PassCollisions == PASS_ALWAYS)
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptCollidingEnd(this.ParentGroup.RootPart, EndCollidingMessage);
                                }
                                else if (((this.ScriptEvents & scriptEvents.collision_end) == 0) && this.PassCollisions == PASS_IF_NOT_HANDLED) //If no event in this prim, pass to parent
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptCollidingEnd(this.ParentGroup.RootPart, EndCollidingMessage);
                                }
                            }
                        }
                    }
                }
                if ((m_parentGroup.RootPart.ScriptEvents & scriptEvents.land_collision_start) != 0)
                {
                    if (startedColliders.Count > 0)
                    {
                        ColliderArgs LandStartCollidingMessage = new ColliderArgs();
                        List<DetectedObject> colliding = new List<DetectedObject>();
                        foreach (uint localId in startedColliders)
                        {
                            if (localId == 0)
                            {
                                //Hope that all is left is ground!
                                DetectedObject detobj = new DetectedObject();
                                detobj.keyUUID = UUID.Zero;
                                detobj.nameStr = "";
                                detobj.ownerUUID = UUID.Zero;
                                detobj.posVector = m_parentGroup.RootPart.AbsolutePosition;
                                detobj.rotQuat = Quaternion.Identity;
                                detobj.velVector = Vector3.Zero;
                                detobj.colliderType = 0;
                                detobj.groupUUID = UUID.Zero;
                                colliding.Add(detobj);
                            }
                        }

                        if (colliding.Count > 0)
                        {
                            LandStartCollidingMessage.Colliders = colliding;
                            // always running this check because if the user deletes the object it would return a null reference.
                            if (m_parentGroup == null)
                                return;

                            if (m_parentGroup.Scene == null)
                                return;

                            m_parentGroup.Scene.EventManager.TriggerScriptLandCollidingStart(this, LandStartCollidingMessage);

                            if ((this.UUID != this.ParentGroup.RootPart.UUID))
                            {
                                const int PASS_IF_NOT_HANDLED = 0;
                                const int PASS_ALWAYS = 1;
                                const int PASS_NEVER = 2;
                                if (this.PassCollisions == PASS_NEVER)
                                {
                                }
                                if (this.PassCollisions == PASS_ALWAYS)
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptLandCollidingStart(this.ParentGroup.RootPart, LandStartCollidingMessage);
                                }
                                else if (((this.ScriptEvents & scriptEvents.land_collision_start) == 0) && this.PassCollisions == PASS_IF_NOT_HANDLED) //If no event in this prim, pass to parent
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptLandCollidingStart(this.ParentGroup.RootPart, LandStartCollidingMessage);
                                }
                            }
                        }
                    }
                }
                if ((m_parentGroup.RootPart.ScriptEvents & scriptEvents.land_collision) != 0)
                {
                    if (m_lastColliders.Count > 0)
                    {
                        ColliderArgs LandCollidingMessage = new ColliderArgs();
                        List<DetectedObject> colliding = new List<DetectedObject>();
                        foreach (uint localId in startedColliders)
                        {
                            if (localId == 0)
                            {
                                //Hope that all is left is ground!
                                DetectedObject detobj = new DetectedObject();
                                detobj.keyUUID = UUID.Zero;
                                detobj.nameStr = "";
                                detobj.ownerUUID = UUID.Zero;
                                detobj.posVector = m_parentGroup.RootPart.AbsolutePosition;
                                detobj.rotQuat = Quaternion.Identity;
                                detobj.velVector = Vector3.Zero;
                                detobj.colliderType = 0;
                                detobj.groupUUID = UUID.Zero;
                                colliding.Add(detobj);
                            }
                        }

                        if (colliding.Count > 0)
                        {
                            LandCollidingMessage.Colliders = colliding;
                            // always running this check because if the user deletes the object it would return a null reference.
                            if (m_parentGroup == null)
                                return;

                            if (m_parentGroup.Scene == null)
                                return;

                            m_parentGroup.Scene.EventManager.TriggerScriptLandColliding(this, LandCollidingMessage);

                            if ((this.UUID != this.ParentGroup.RootPart.UUID))
                            {
                                const int PASS_IF_NOT_HANDLED = 0;
                                const int PASS_ALWAYS = 1;
                                const int PASS_NEVER = 2;
                                if (this.PassCollisions == PASS_NEVER)
                                {
                                }
                                if (this.PassCollisions == PASS_ALWAYS)
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptColliding(this.ParentGroup.RootPart, LandCollidingMessage);
                                }
                                else if (((this.ScriptEvents & scriptEvents.land_collision) == 0) && this.PassCollisions == PASS_IF_NOT_HANDLED) //If no event in this prim, pass to parent
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptColliding(this.ParentGroup.RootPart, LandCollidingMessage);
                                }
                            }
                        }
                    }
                }
                if ((m_parentGroup.RootPart.ScriptEvents & scriptEvents.land_collision_end) != 0)
                {
                    if (endedColliders.Count > 0)
                    {
                        ColliderArgs LandEndCollidingMessage = new ColliderArgs();
                        List<DetectedObject> colliding = new List<DetectedObject>();
                        foreach (uint localId in startedColliders)
                        {
                            if (localId == 0)
                            {
                                //Hope that all is left is ground!
                                DetectedObject detobj = new DetectedObject();
                                detobj.keyUUID = UUID.Zero;
                                detobj.nameStr = "";
                                detobj.ownerUUID = UUID.Zero;
                                detobj.posVector = m_parentGroup.RootPart.AbsolutePosition;
                                detobj.rotQuat = Quaternion.Identity;
                                detobj.velVector = Vector3.Zero;
                                detobj.colliderType = 0;
                                detobj.groupUUID = UUID.Zero;
                                colliding.Add(detobj);
                            }
                        }

                        if (colliding.Count > 0)
                        {
                            LandEndCollidingMessage.Colliders = colliding;
                            // always running this check because if the user deletes the object it would return a null reference.
                            if (m_parentGroup == null)
                                return;

                            if (m_parentGroup.Scene == null)
                                return;

                            m_parentGroup.Scene.EventManager.TriggerScriptLandCollidingEnd(this, LandEndCollidingMessage);

                            if ((this.UUID != this.ParentGroup.RootPart.UUID))
                            {
                                const int PASS_IF_NOT_HANDLED = 0;
                                const int PASS_ALWAYS = 1;
                                const int PASS_NEVER = 2;
                                if (this.PassCollisions == PASS_NEVER)
                                {
                                }
                                if (this.PassCollisions == PASS_ALWAYS)
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptLandCollidingEnd(this.ParentGroup.RootPart, LandEndCollidingMessage);
                                }
                                else if (((this.ScriptEvents & scriptEvents.land_collision_end) == 0) && this.PassCollisions == PASS_IF_NOT_HANDLED) //If no event in this prim, pass to parent
                                {
                                    m_parentGroup.Scene.EventManager.TriggerScriptLandCollidingEnd(this.ParentGroup.RootPart, LandEndCollidingMessage);
                                }
                            }
                        }
                    }
                }
            }
        }

        public void PhysicsOutOfBounds(Vector3 pos)
        {
            m_log.Error("[Physics]: Physical Object " + Name + " @ " + AbsolutePosition + " went out of bounds.");
            if(!ParentGroup.Scene.PhysicsReturns.Contains(ParentGroup))
                ParentGroup.Scene.PhysicsReturns.Add(ParentGroup);
        }

        public void PhysicsRequestingTerseUpdate()
        {
            if (PhysActor != null)
            {
//                Vector3 newpos = new Vector3(PhysActor.Position.GetBytes(), 0);
                m_parentGroup.SetAbsolutePosition(false,PhysActor.Position);
                //m_parentGroup.RootPart.m_groupPosition = newpos;
            }
            ScheduleTerseUpdate();
        }

        public void PreloadSound(string sound)
        {
            // UUID ownerID = OwnerID;
            UUID objectID = ParentGroup.RootPart.UUID;
            UUID soundID = UUID.Zero;

            if (!UUID.TryParse(sound, out soundID))
            {
                //Trys to fetch sound id from prim's inventory.
                //Prim's inventory doesn't support non script items yet
                
                lock (TaskInventory)
                {
                    foreach (KeyValuePair<UUID, TaskInventoryItem> item in TaskInventory)
                    {
                        if (item.Value.Name == sound)
                        {
                            soundID = item.Value.ItemID;
                            break;
                        }
                    }
                }
            }

            m_parentGroup.Scene.ForEachScenePresence(delegate(ScenePresence sp)
            {
                if (sp.IsChildAgent)
                    return;
                if (!(Util.GetDistanceTo(sp.AbsolutePosition, AbsolutePosition) >= 100))
                    sp.ControllingClient.SendPreLoadSound(objectID, objectID, soundID);
            });
        }

        public bool RemFlag(PrimFlags flag)
        {
            // PrimFlags prevflag = Flags;
            if ((Flags & flag) != 0)
            {
                //m_log.Debug("Removing flag: " + ((PrimFlags)flag).ToString());
                Flags &= ~flag;
                return true;
            }
            return false;
            //m_log.Debug("prev: " + prevflag.ToString() + " curr: " + Flags.ToString());
            //ScheduleFullUpdate();
        }
        
        public void RemoveScriptEvents(UUID scriptid)
        {
            lock (m_scriptEvents)
            {
                if (m_scriptEvents.ContainsKey(scriptid))
                {
                    scriptEvents oldparts = scriptEvents.None;
                    oldparts = (scriptEvents) m_scriptEvents[scriptid];

                    // remove values from aggregated script events
                    AggregateScriptEvents &= ~oldparts;
                    m_scriptEvents.Remove(scriptid);
                    aggregateScriptEvents();
                }
            }
        }

        public void ResetEntityIDs()
        {
            UUID = UUID.Random();
            //LinkNum = linkNum;
            Inventory.ResetInventoryIDs(false);
            LocalId = ParentGroup.Scene.SceneGraph.AllocateLocalId();

            //Fix the localID now for the physics engine
            if (m_physActor != null)
                m_physActor.LocalID = LocalId;
            //Fix the rezzed attribute
            Rezzed = DateTime.UtcNow;
            //TODO: Check to make sure the physics engine is fully updated here

            //Don't reset this for now
            //CRC = 0;
        }

        /// <summary>
        /// Resize this part.
        /// </summary>
        /// <param name="scale"></param>
        public void Resize(Vector3 scale)
        {
            Scale = scale;

            ParentGroup.HasGroupChanged = true;
            ScheduleUpdate(PrimUpdateFlags.Shape);
        }
        
        public void RotLookAt(Quaternion target, float strength, float damping)
        {
            if (IsAttachment)
            {
                /*
                    ScenePresence avatar = m_scene.GetScenePresence(rootpart.AttachedAvatar);
                    if (avatar != null)
                    {
                    Rotate the Av?
                    } */
            }
            else
            {
                APIDDamp = damping;
                APIDStrength = strength;
                APIDTarget = target;
            }
        }

        public void startLookAt(Quaternion rot, float damp, float strength)
        {
            APIDDamp = damp;
            APIDStrength = strength;
            APIDTarget = rot;
        }

        public void stopLookAt()
        {
            APIDTarget = Quaternion.Identity;
        }

        /// <summary>
        /// Clear all pending updates of parts to clients
        /// NOTE: Do NOT use this for things that reuse the LocalID, as it will break the object
        /// </summary>
        public void ClearUpdateSchedule()
        {
            foreach (ScenePresence SP in ParentGroup.Scene.ScenePresences)
            {
                SP.SceneViewer.ClearUpdatesForPart(this);
            }
        }

        /// <summary>
        /// Clear all pending updates of parts to clients once.
        /// NOTE: Use this for linking and other things that are going to be reusing the same LocalID
        /// </summary>
        public void ClearUpdateScheduleOnce()
        {
            foreach (ScenePresence SP in ParentGroup.Scene.ScenePresences)
            {
                SP.SceneViewer.ClearUpdatesForOneLoopForPart(this);
            }
        }

        /// <summary>
        /// Schedule a terse update for this prim.  Terse updates only send position,
        /// rotation, velocity, and rotational velocity information.
        /// </summary>
        public void ScheduleTerseUpdate()
        {
            PrimUpdateFlags UpdateFlags = PrimUpdateFlags.Position | PrimUpdateFlags.Rotation | PrimUpdateFlags.Velocity | PrimUpdateFlags.Acceleration | PrimUpdateFlags.AngularVelocity;
            ScheduleUpdate(UpdateFlags);
        }

        /// <summary>
        /// Check to see whether the given flags make it a terse update
        /// </summary>
        /// <param name="flags"></param>
        /// <returns></returns>
        private bool IsTerse(PrimUpdateFlags flags)
        {
            return flags.HasFlag((PrimUpdateFlags.Position | PrimUpdateFlags.Rotation
                | PrimUpdateFlags.Velocity | PrimUpdateFlags.Acceleration | PrimUpdateFlags.AngularVelocity))
                && !flags.HasFlag((PrimUpdateFlags.AttachmentPoint | PrimUpdateFlags.ClickAction |
                PrimUpdateFlags.CollisionPlane | PrimUpdateFlags.ExtraData | PrimUpdateFlags.FindBest | PrimUpdateFlags.FullUpdate |
                PrimUpdateFlags.Joint | PrimUpdateFlags.Material | PrimUpdateFlags.MediaURL | PrimUpdateFlags.NameValue |
                PrimUpdateFlags.ParentID | PrimUpdateFlags.Particles | PrimUpdateFlags.PrimData | PrimUpdateFlags.PrimFlags |
                PrimUpdateFlags.ScratchPad | PrimUpdateFlags.Shape | PrimUpdateFlags.Sound | PrimUpdateFlags.Text |
                PrimUpdateFlags.TextureAnim | PrimUpdateFlags.Textures));
        }

        /// <summary>
        /// Tell all avatars in the Scene about the new update
        /// </summary>
        /// <param name="UpdateFlags"></param>
        public void ScheduleUpdate(PrimUpdateFlags UpdateFlags)
        {
            PrimUpdateFlags PostUpdateFlags;
            if (ShouldScheduleUpdate(UpdateFlags, out PostUpdateFlags))
            {
                m_parentGroup.Scene.ForEachScenePresence(delegate(ScenePresence avatar)
                {
                    avatar.AddUpdateToAvatar(this, PostUpdateFlags);
                });
            }
        }

        /// <summary>
        /// Tell a specific avatar about the update
        /// </summary>
        /// <param name="UpdateFlags"></param>
        /// <param name="avatar"></param>
        public void ScheduleUpdateToAvatar(PrimUpdateFlags UpdateFlags, ScenePresence avatar)
        {
            PrimUpdateFlags PostUpdateFlags;
            if (ShouldScheduleUpdate(UpdateFlags, out PostUpdateFlags))
            {
                avatar.AddUpdateToAvatar(this, PostUpdateFlags);
            }
        }

        /// <summary>
        /// Make sure that we should send the specified update to the client
        /// </summary>
        /// <param name="UpdateFlags"></param>
        /// <returns></returns>
        protected bool ShouldScheduleUpdate(PrimUpdateFlags UpdateFlags, out PrimUpdateFlags PostUpdateFlags)
        {
            PostUpdateFlags = UpdateFlags;
            //If its not a terse update, we need to make sure to add the text, media, etc pieces on, otherwise the client will forget about them
            if (!IsTerse(UpdateFlags))
            {
                //If it is find best, we add the defaults
                if (UpdateFlags == PrimUpdateFlags.FindBest)
                {
                    //Add the defaults
                    UpdateFlags = PrimUpdateFlags.None;
                    UpdateFlags |= PrimUpdateFlags.ClickAction;
                    UpdateFlags |= PrimUpdateFlags.ExtraData;
                    UpdateFlags |= PrimUpdateFlags.Shape;
                    UpdateFlags |= PrimUpdateFlags.Material;
                    UpdateFlags |= PrimUpdateFlags.Textures;
                    UpdateFlags |= PrimUpdateFlags.Rotation;
                    UpdateFlags |= PrimUpdateFlags.PrimFlags;
                    UpdateFlags |= PrimUpdateFlags.Position;
                    UpdateFlags |= PrimUpdateFlags.AngularVelocity;
                }

                //Must send these as well
                if (Text != "")
                    UpdateFlags |= PrimUpdateFlags.Text;
                if (AngularVelocity != Vector3.Zero)
                    UpdateFlags |= PrimUpdateFlags.AngularVelocity;
                if (TextureAnimation != null && TextureAnimation.Length != 0)
                    UpdateFlags |= PrimUpdateFlags.TextureAnim;
                if (Sound != UUID.Zero)
                    UpdateFlags |= PrimUpdateFlags.Sound;
                if (ParticleSystem != null && ParticleSystem.Length != 0)
                    UpdateFlags |= PrimUpdateFlags.Particles;
                if (MediaUrl != "" && MediaUrl != null)
                    UpdateFlags |= PrimUpdateFlags.MediaURL;
                if (ParentGroup.RootPart.IsAttachment)
                    UpdateFlags |= PrimUpdateFlags.AttachmentPoint;

                //Make sure that we send this! Otherwise, the client will only see one prim
                if (m_parentGroup != null)
                    if (ParentGroup.ChildrenList.Count != 1)
                        UpdateFlags |= PrimUpdateFlags.ParentID;

                //Increment the CRC code so that the client won't be sent a cached update for this
                CRC++;
            }

            if (ParentGroup != null)
            {
                if (ParentGroup.Scene == null) // Need to check here as it's null during object creation
                    return false;

                // Check that the group was not deleted before the scheduled update
                // FIXME: This is merely a temporary measure to reduce the incidence of failure when
                // an object has been deleted from a scene before update was processed.
                // A more fundamental overhaul of the update mechanism is required to eliminate all
                // the race conditions.
                if (ParentGroup.IsDeleted)
                    return false;

                if (IsTerse(UpdateFlags))
                {
                    const float ROTATION_TOLERANCE = 0.01f;
                    const float VELOCITY_TOLERANCE = 0.001f;
                    const float POSITION_TOLERANCE = 0.01f;

                    // Throw away duplicate or insignificant updates
                    if (!RotationOffset.ApproxEquals(m_lastRotation, ROTATION_TOLERANCE) ||
                        !Acceleration.Equals(m_lastAcceleration) ||
                        !Velocity.ApproxEquals(m_lastVelocity, VELOCITY_TOLERANCE) ||
                        !Velocity.ApproxEquals(Vector3.Zero, VELOCITY_TOLERANCE) ||
                        !AngularVelocity.ApproxEquals(m_lastAngularVelocity, VELOCITY_TOLERANCE) ||
                        !OffsetPosition.ApproxEquals(m_lastPosition, POSITION_TOLERANCE) ||
                        !GroupPosition.ApproxEquals(m_lastGroupPosition, POSITION_TOLERANCE))
                    {
                        // Update the "last" values
                        m_lastPosition = OffsetPosition;
                        m_lastGroupPosition = GroupPosition;
                        m_lastRotation = RotationOffset;
                        m_lastVelocity = Velocity;
                        m_lastAcceleration = Acceleration;
                        m_lastAngularVelocity = AngularVelocity;
                    }
                    else
                    {
                        IMonitorModule m = ParentGroup.Scene.RequestModuleInterface<IMonitorModule>();
                        if (m != null)
                            ((IObjectUpdateMonitor)m.GetMonitor(ParentGroup.Scene.RegionInfo.RegionID.ToString(), "PrimUpdates")).AddLimitedPrims(1);
                        return false;
                    }
                }
                //Reupdate so they get sent properly
                PostUpdateFlags = UpdateFlags;
                return true;
            }
            return false;
        }

        public void ScriptSetPhantomStatus(bool Phantom)
        {
            if (m_parentGroup != null)
            {
                m_parentGroup.ScriptSetPhantomStatus(Phantom);
            }
        }

        public void ScriptSetTemporaryStatus(bool Temporary)
        {
            if (m_parentGroup != null)
            {
                m_parentGroup.ScriptSetTemporaryStatus(Temporary);
            }
        }

        public void ScriptSetPhysicsStatus(bool UsePhysics)
        {
            if (m_parentGroup == null)
                DoPhysicsPropertyUpdate(UsePhysics, false);
            else
                m_parentGroup.ScriptSetPhysicsStatus(UsePhysics);
        }

        public void ScriptSetVolumeDetect(bool SetVD)
        {

            if (m_parentGroup != null)
            {
                m_parentGroup.ScriptSetVolumeDetect(SetVD);
            }
        }


        public void SculptTextureCallback(UUID textureID, AssetBase texture)
        {
            if (m_shape.SculptEntry)
            {
                // commented out for sculpt map caching test - null could mean a cached sculpt map has been found
                //if (texture != null)
                {
                    if (texture != null)
                        m_shape.SculptData = texture.Data;

                    if (PhysActor != null)
                    {
                        // Tricks physics engine into thinking we've changed the part shape.
                        PrimitiveBaseShape m_newshape = m_shape.Copy();
                        PhysActor.Shape = m_newshape;
                        m_shape = m_newshape;

                        m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPhysicsActorTaint(PhysActor);
                    }
                }
            }
        }

        /// <summary>
        /// Trigger or play an attached sound in this part's inventory.
        /// </summary>
        /// <param name="sound"></param>
        /// <param name="volume"></param>
        /// <param name="triggered"></param>
        /// <param name="flags"></param>
        public void SendSound(string sound, double volume, bool triggered, byte flags, float radius, bool useMaster, bool isMaster)
        {
            if (volume > 1)
                volume = 1;
            if (volume < 0)
                volume = 0;

            UUID ownerID = _ownerID;
            UUID objectID = ParentGroup.RootPart.UUID;
            UUID parentID = GetRootPartUUID();
            UUID soundID = UUID.Zero;
            Vector3 position = AbsolutePosition; // region local
            ulong regionHandle = m_parentGroup.Scene.RegionInfo.RegionHandle;

            if (!UUID.TryParse(sound, out soundID))
            {
                // search sound file from inventory
                lock (TaskInventory)
                {
                    foreach (KeyValuePair<UUID, TaskInventoryItem> item in TaskInventory)
                    {
                        if (item.Value.Name == sound && item.Value.Type == (int)AssetType.Sound)
                        {
                            soundID = item.Value.ItemID;
                            break;
                        }
                    }
                }
            }

            if (soundID == UUID.Zero)
                return;

            ISoundModule soundModule = m_parentGroup.Scene.RequestModuleInterface<ISoundModule>();
            if (soundModule != null)
            {
                if (useMaster)
                {
                    if (isMaster)
                    {
                        if (triggered)
                            soundModule.TriggerSound(soundID, ownerID, objectID, parentID, volume, position, regionHandle, radius);
                        else
                            soundModule.PlayAttachedSound(soundID, ownerID, objectID, volume, position, flags, radius);
                        ParentGroup.PlaySoundMasterPrim = this;
                        ownerID = _ownerID;
                        objectID = ParentGroup.RootPart.UUID;
                        parentID = GetRootPartUUID();
                        position = AbsolutePosition; // region local
                        regionHandle = ParentGroup.Scene.RegionInfo.RegionHandle;
                        if (triggered)
                            soundModule.TriggerSound(soundID, ownerID, objectID, parentID, volume, position, regionHandle, radius);
                        else
                            soundModule.PlayAttachedSound(soundID, ownerID, objectID, volume, position, flags, radius);
                        foreach (SceneObjectPart prim in ParentGroup.PlaySoundSlavePrims)
                        {
                            ownerID = prim._ownerID;
                            objectID = prim.ParentGroup.RootPart.UUID;
                            parentID = prim.GetRootPartUUID();
                            position = prim.AbsolutePosition; // region local
                            regionHandle = prim.ParentGroup.Scene.RegionInfo.RegionHandle;
                            if (triggered)
                                soundModule.TriggerSound(soundID, ownerID, objectID, parentID, volume, position, regionHandle, radius);
                            else
                                soundModule.PlayAttachedSound(soundID, ownerID, objectID, volume, position, flags, radius);
                        }
                        ParentGroup.PlaySoundSlavePrims.Clear();
                        ParentGroup.PlaySoundMasterPrim = null;
                    }
                    else
                    {
                        ParentGroup.PlaySoundSlavePrims.Add(this);
                    }
                }
                else
                {
                    if (triggered)
                        soundModule.TriggerSound(soundID, ownerID, objectID, parentID, volume, position, regionHandle, radius);
                    else
                        soundModule.PlayAttachedSound(soundID, ownerID, objectID, volume, position, flags, radius);
                }
            }
        }

        public void SetAttachmentPoint(int AttachmentPoint)
        {
            //Update the saved if needed
            if (AttachmentPoint == 0 && this.AttachmentPoint != 0)
            {
                this.SavedAttachedPos = this.AttachedPos;
                this.SavedAttachmentPoint = this.AttachmentPoint;
            }

            this.AttachmentPoint = AttachmentPoint;

            if (AttachmentPoint != 0)
            {
                IsAttachment = true;
            }
            else
            {
                IsAttachment = false;
            }

            // save the attachment point.
            //if (AttachmentPoint != 0)
            //{
                m_shape.State = (byte)AttachmentPoint;
            //}
        }

        public void SetAvatarOnSitTarget(UUID avatarID)
        {
            if (ParentGroup != null)
            {
                ParentGroup.TriggerSetSitAvatarUUID(avatarID);
                ParentGroup.TriggerScriptChangedEvent(Changed.LINK);
            }
        }

        public void RemoveAvatarOnSitTarget(UUID avatarID)
        {
            if (ParentGroup != null)
            {
                ParentGroup.TriggerRemoveSitAvatarUUID(avatarID);
                ParentGroup.TriggerScriptChangedEvent(Changed.LINK);
            }
        }

        public void SetAxisRotation(int axis, int rotate)
        {
            if (m_parentGroup != null)
            {
                m_parentGroup.SetAxisRotation(axis, rotate);
            }
            //Cannot use ScriptBaseClass constants as no referance to it currently.
            if ((axis & 2) == 2)//STATUS_ROTATE_X
                STATUS_ROTATE_X = rotate;
            if ((axis & 4) == 4)//STATUS_ROTATE_Y
                STATUS_ROTATE_Y = rotate;
            if ((axis & 8) == 8)//STATUS_ROTATE_Z
                STATUS_ROTATE_Z = rotate;
        }

        public void SetBuoyancy(float fvalue)
        {
            if (PhysActor != null)
            {
                PhysActor.Buoyancy = fvalue;
            }
        }

        public void SetDieAtEdge(bool p)
        {
            if (m_parentGroup == null)
                return;
            if (m_parentGroup.IsDeleted)
                return;

            m_parentGroup.RootPart.DIE_AT_EDGE = p;
        }

        public void SetFloatOnWater(int floatYN)
        {
            if (PhysActor != null)
            {
                if (floatYN == 1)
                {
                    PhysActor.FloatOnWater = true;
                }
                else
                {
                    PhysActor.FloatOnWater = false;
                }
            }
        }

        public void SetForce(Vector3 force)
        {
            if (PhysActor != null)
            {
                PhysActor.Force = force;
            }
        }

        public void SetVehicleType(int type)
        {
            if (PhysActor != null)
            {
                PhysActor.VehicleType = type;
            }
        }

        public void SetVehicleFloatParam(int param, float value)
        {
            if (PhysActor != null)
            {
                PhysActor.VehicleFloatParam(param, value);
            }
        }

        public void SetVehicleVectorParam(int param, Vector3 value)
        {
            if (PhysActor != null)
            {
                PhysActor.VehicleVectorParam(param, value);
            }
        }

        public void SetVehicleRotationParam(int param, Quaternion rotation)
        {
            if (PhysActor != null)
            {
                PhysActor.VehicleRotationParam(param, rotation);
            }
        }

        public void SetPhysActorCameraPos(Vector3 CameraRotation)
        {
            if (PhysActor != null)
            {
                PhysActor.SetCameraPos(CameraRotation);
            }
        }

        /// <summary>
        /// Set the color of prim faces
        /// </summary>
        /// <param name="color"></param>
        /// <param name="face"></param>
        public void SetFaceColor(Vector3 color, int face)
        {
            Primitive.TextureEntry tex = Shape.Textures;
            Color4 texcolor;
            if (face >= 0 && face < GetNumberOfSides())
            {
                texcolor = tex.CreateFace((uint)face).RGBA;
                texcolor.R = Util.Clip((float)color.X, 0.0f, 1.0f);
                texcolor.G = Util.Clip((float)color.Y, 0.0f, 1.0f);
                texcolor.B = Util.Clip((float)color.Z, 0.0f, 1.0f);
                tex.FaceTextures[face].RGBA = texcolor;
                UpdateTexture(tex);
				//WRONG.... fixed with updateTexture
                //TriggerScriptChangedEvent(Changed.COLOR);
                return;
            }
            else if (face == ALL_SIDES)
            {
                for (uint i = 0; i < GetNumberOfSides(); i++)
                {
                    if (tex.FaceTextures[i] != null)
                    {
                        texcolor = tex.FaceTextures[i].RGBA;
                        texcolor.R = Util.Clip((float)color.X, 0.0f, 1.0f);
                        texcolor.G = Util.Clip((float)color.Y, 0.0f, 1.0f);
                        texcolor.B = Util.Clip((float)color.Z, 0.0f, 1.0f);
                        tex.FaceTextures[i].RGBA = texcolor;
                    }
                    texcolor = tex.DefaultTexture.RGBA;
                    texcolor.R = Util.Clip((float)color.X, 0.0f, 1.0f);
                    texcolor.G = Util.Clip((float)color.Y, 0.0f, 1.0f);
                    texcolor.B = Util.Clip((float)color.Z, 0.0f, 1.0f);
                    tex.DefaultTexture.RGBA = texcolor;
                }
                UpdateTexture(tex);
				//WRONG.... fixed with updateTexture
                //TriggerScriptChangedEvent(Changed.COLOR);
                return;
            }
        }

        /// <summary>
        /// Get the number of sides that this part has.
        /// </summary>
        /// <returns></returns>
        public int GetNumberOfSides()
        {
            int ret = 0;
            bool hasCut;
            bool hasHollow;
            bool hasDimple;
            bool hasProfileCut;

            PrimType primType = GetPrimType();
            HasCutHollowDimpleProfileCut(primType, Shape, out hasCut, out hasHollow, out hasDimple, out hasProfileCut);

            switch (primType)
            {
                case PrimType.BOX:
                    ret = 6;
                    if (hasCut) ret += 2;
                    if (hasHollow) ret += 1;
                    break;
                case PrimType.CYLINDER:
                    ret = 3;
                    if (hasCut) ret += 2;
                    if (hasHollow) ret += 1;
                    break;
                case PrimType.PRISM:
                    ret = 5;
                    if (hasCut) ret += 2;
                    if (hasHollow) ret += 1;
                    break;
                case PrimType.SPHERE:
                    ret = 1;
                    if (hasCut) ret += 2;
                    if (hasDimple) ret += 2;
                    if (hasHollow) ret += 1;
                    break;
                case PrimType.TORUS:
                    ret = 1;
                    if (hasCut) ret += 2;
                    if (hasProfileCut) ret += 2;
                    if (hasHollow) ret += 1;
                    break;
                case PrimType.TUBE:
                    ret = 4;
                    if (hasCut) ret += 2;
                    if (hasProfileCut) ret += 2;
                    if (hasHollow) ret += 1;
                    break;
                case PrimType.RING:
                    ret = 3;
                    if (hasCut) ret += 2;
                    if (hasProfileCut) ret += 2;
                    if (hasHollow) ret += 1;
                    break;
                case PrimType.SCULPT:
                    ret = 1;
                    break;
            }
            return ret;
        }

        /// <summary>
        /// Tell us what type this prim is
        /// </summary>
        /// <param name="primShape"></param>
        /// <returns></returns>
        public PrimType GetPrimType()
        {
            if (Shape.SculptEntry)
                return PrimType.SCULPT;
            if ((Shape.ProfileCurve & 0x07) == (byte)ProfileShape.Square)
            {
                if (Shape.PathCurve == (byte)Extrusion.Straight)
                    return PrimType.BOX;
                else if (Shape.PathCurve == (byte)Extrusion.Curve1)
                    return PrimType.TUBE;
            }
            else if ((Shape.ProfileCurve & 0x07) == (byte)ProfileShape.Circle)
            {
                if (Shape.PathCurve == (byte)Extrusion.Straight)
                    return PrimType.CYLINDER;
                // ProfileCurve seems to combine hole shape and profile curve so we need to only compare against the lower 3 bits
                else if (Shape.PathCurve == (byte)Extrusion.Curve1)
                    return PrimType.TORUS;
            }
            else if ((Shape.ProfileCurve & 0x07) == (byte)ProfileShape.HalfCircle)
            {
                if (Shape.PathCurve == (byte)Extrusion.Curve1 || Shape.PathCurve == (byte)Extrusion.Curve2)
                    return PrimType.SPHERE;
            }
            else if ((Shape.ProfileCurve & 0x07) == (byte)ProfileShape.EquilateralTriangle)
            {
                if (Shape.PathCurve == (byte)Extrusion.Straight)
                    return PrimType.PRISM;
                else if (Shape.PathCurve == (byte)Extrusion.Curve1)
                    return PrimType.RING;
            }
            
            return PrimType.BOX;
        }
        
        /// <summary>
        /// Tell us if this object has cut, hollow, dimple, and other factors affecting the number of faces 
        /// </summary>
        /// <param name="primType"></param>
        /// <param name="shape"></param>
        /// <param name="hasCut"></param>
        /// <param name="hasHollow"></param>
        /// <param name="hasDimple"></param>
        /// <param name="hasProfileCut"></param>
        protected static void HasCutHollowDimpleProfileCut(PrimType primType, PrimitiveBaseShape shape, out bool hasCut, out bool hasHollow,
            out bool hasDimple, out bool hasProfileCut)
        {
            if (primType == PrimType.BOX
                ||
                primType == PrimType.CYLINDER
                ||
                primType == PrimType.PRISM)

                hasCut = (shape.ProfileBegin > 0) || (shape.ProfileEnd > 0);
            else
                hasCut = (shape.PathBegin > 0) || (shape.PathEnd > 0);

            hasHollow = shape.ProfileHollow > 0;
            hasDimple = (shape.ProfileBegin > 0) || (shape.ProfileEnd > 0); // taken from llSetPrimitiveParms
            hasProfileCut = hasDimple; // is it the same thing?
        }
        
        public void SetVehicleFlags(int param, bool remove)
        {
            if (PhysActor != null)
            {
                PhysActor.VehicleFlags(param, remove);
            }
        }

        public void SetGroup(UUID groupID, IClientAPI client)
        {
            _groupID = groupID;
            if (client != null)
                GetProperties(client);
            ScheduleUpdate(PrimUpdateFlags.FullUpdate);
        }

        /// <summary>
        ///
        /// </summary>
        public void SetParent(SceneObjectGroup parent)
        {
            m_parentGroup = parent;
        }

        // Use this for attachments!  LocalID should be avatar's localid
        public void SetParentLocalId(uint localID)
        {
            _parentID = localID;
        }

        public void SetPhysicsAxisRotation()
        {
            if (PhysActor != null)
            {
                PhysActor.LockAngularMotion(RotationAxis);
                m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPhysicsActorTaint(PhysActor);
            }
        }

        /// <summary>
        /// Set the events that this part will pass on to listeners.
        /// </summary>
        /// <param name="scriptid"></param>
        /// <param name="events"></param>
        public void SetScriptEvents(UUID scriptid, long events)
        {
            // scriptEvents oldparts;
            lock (m_scriptEvents)
            {
                if (m_scriptEvents.ContainsKey(scriptid))
                {
                    // oldparts = m_scriptEvents[scriptid];

                    // remove values from aggregated script events
                    if (m_scriptEvents[scriptid] == (scriptEvents) events)
                        return;
                    m_scriptEvents[scriptid] = (scriptEvents) events;
                }
                else
                {
                    m_scriptEvents.Add(scriptid, (scriptEvents) events);
                }
            }
            aggregateScriptEvents();
        }

        /// <summary>
        /// Set the text displayed for this part.
        /// </summary>
        /// <param name="text"></param>
        public void SetText(string text)
        {
            if (Text != text)
                ParentGroup.HasGroupChanged = true;
            Text = text;

            ScheduleUpdate(PrimUpdateFlags.Text);
        }
        
        public void StopLookAt()
        {
            m_parentGroup.stopLookAt();

            m_parentGroup.ScheduleGroupTerseUpdate();
        }
        
        /// <summary>
        /// Set the text displayed for this part.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="color"></param>
        /// <param name="alpha"></param>
        public void SetText(string text, Vector3 color, double alpha)
        {
            //No triggering Changed_Color, so not using Color
            //Color = ...
            m_color = Color.FromArgb((int)(alpha * 0xff),
                                   (int) (color.X*0xff),
                                   (int) (color.Y*0xff),
                                   (int) (color.Z*0xff));
            SetText(text);
        }

        public void StopMoveToTarget()
        {
            m_parentGroup.stopMoveToTarget();

            m_parentGroup.ScheduleGroupTerseUpdate();
            //m_parentGroup.ScheduleGroupForFullUpdate();
        }

        public void StoreUndoState()
        {
            if (!Undoing)
            {
                if (!IgnoreUndoUpdate)
                {
                    IBackupModule backup = null;
                    if(ParentGroup != null && 
                        ParentGroup.Scene != null)
                        backup = ParentGroup.Scene.RequestModuleInterface<IBackupModule>();

                    if (m_parentGroup != null && 
                        ParentGroup.Scene != null &&
                        (backup == null || (backup != null && !backup.LoadingPrims)))
                    {
                        lock (m_undo)
                        {
                            if (m_undo.Count > 0)
                            {
                                UndoState last = m_undo.Peek();
                                if (last != null)
                                {
                                    if (last.Compare(this))
                                        return;
                                }
                            }

                            UndoState nUndo = new UndoState(this);
                            m_undo.Push(nUndo);
                        }
                    }
                }
            }
        }
/* not in use
        public EntityIntersection TestIntersection(Ray iray, Quaternion parentrot)
        {
            // In this case we're using a sphere with a radius of the largest dimension of the prim
            // TODO: Change to take shape into account

            EntityIntersection result = new EntityIntersection();
            Vector3 vAbsolutePosition = AbsolutePosition;
            Vector3 vScale = Scale;
            Vector3 rOrigin = iray.Origin;
            Vector3 rDirection = iray.Direction;

            //rDirection = rDirection.Normalize();
            // Buidling the first part of the Quadratic equation
            Vector3 r2ndDirection = rDirection*rDirection;
            float itestPart1 = r2ndDirection.X + r2ndDirection.Y + r2ndDirection.Z;

            // Buidling the second part of the Quadratic equation
            Vector3 tmVal2 = rOrigin - vAbsolutePosition;
            Vector3 r2Direction = rDirection*2.0f;
            Vector3 tmVal3 = r2Direction*tmVal2;

            float itestPart2 = tmVal3.X + tmVal3.Y + tmVal3.Z;

            // Buidling the third part of the Quadratic equation
            Vector3 tmVal4 = rOrigin*rOrigin;
            Vector3 tmVal5 = vAbsolutePosition*vAbsolutePosition;

            Vector3 tmVal6 = vAbsolutePosition*rOrigin;

            // Set Radius to the largest dimension of the prim
            float radius = 0f;
            if (vScale.X > radius)
                radius = vScale.X;
            if (vScale.Y > radius)
                radius = vScale.Y;
            if (vScale.Z > radius)
                radius = vScale.Z;

            // the second part of this is the default prim size
            // once we factor in the aabb of the prim we're adding we can
            // change this to;
            // radius = (radius / 2) - 0.01f;
            //
            radius = (radius / 2) + (0.25f) - 0.1f;

            //radius = radius;

            float itestPart3 = tmVal4.X + tmVal4.Y + tmVal4.Z + tmVal5.X + tmVal5.Y + tmVal5.Z -
                               (2.0f*(tmVal6.X + tmVal6.Y + tmVal6.Z + (radius*radius)));

            // Yuk Quadradrics..    Solve first
            float rootsqr = (itestPart2*itestPart2) - (4.0f*itestPart1*itestPart3);
            if (rootsqr < 0.0f)
            {
                // No intersection
                return result;
            }
            float root = ((-itestPart2) - (float) Math.Sqrt((double) rootsqr))/(itestPart1*2.0f);

            if (root < 0.0f)
            {
                // perform second quadratic root solution
                root = ((-itestPart2) + (float) Math.Sqrt((double) rootsqr))/(itestPart1*2.0f);

                // is there any intersection?
                if (root < 0.0f)
                {
                    // nope, no intersection
                    return result;
                }
            }

            // We got an intersection.  putting together an EntityIntersection object with the
            // intersection information
            Vector3 ipoint =
                new Vector3(rOrigin.X + (rDirection.X * root), rOrigin.Y + (rDirection.Y * root),
                            rOrigin.Z + (rDirection.Z * root));

            result.HitTF = true;
            result.ipoint = ipoint;

            // Normal is calculated by the difference and then normalizing the result
            Vector3 normalpart = ipoint - vAbsolutePosition;
            result.normal = normalpart / normalpart.Length();

            // It's funny how the Vector3 object has a Distance function, but the Axiom.Math object doesn't.
            // I can write a function to do it..    but I like the fact that this one is Static.

//            Vector3 distanceConvert1 = new Vector3(iray.Origin.X, iray.Origin.Y, iray.Origin.Z);
//            Vector3 distanceConvert2 = new Vector3(ipoint.X, ipoint.Y, ipoint.Z);
//
            float distance = (float)Util.GetDistanceTo(rOrigin, ipoint);

            result.distance = distance;

            return result;
        }
*/
        public EntityIntersection TestIntersectionOBB(Ray iray, Quaternion parentrot, bool frontFacesOnly, bool faceCenters)
        {
            // In this case we're using a rectangular prism, which has 6 faces and therefore 6 planes
            // This breaks down into the ray---> plane equation.
            // TODO: Change to take shape into account
            Vector3[] vertexes = new Vector3[8];

            // float[] distance = new float[6];
            Vector3[] FaceA = new Vector3[6]; // vertex A for Facei
            Vector3[] FaceB = new Vector3[6]; // vertex B for Facei
            Vector3[] FaceC = new Vector3[6]; // vertex C for Facei
            Vector3[] FaceD = new Vector3[6]; // vertex D for Facei

            Vector3[] normals = new Vector3[6]; // Normal for Facei
            Vector3[] AAfacenormals = new Vector3[6]; // Axis Aligned face normals

            AAfacenormals[0] = new Vector3(1, 0, 0);
            AAfacenormals[1] = new Vector3(0, 1, 0);
            AAfacenormals[2] = new Vector3(-1, 0, 0);
            AAfacenormals[3] = new Vector3(0, -1, 0);
            AAfacenormals[4] = new Vector3(0, 0, 1);
            AAfacenormals[5] = new Vector3(0, 0, -1);

            Vector3 AmBa = new Vector3(0, 0, 0); // Vertex A - Vertex B
            Vector3 AmBb = new Vector3(0, 0, 0); // Vertex B - Vertex C
            Vector3 cross = new Vector3();

            Vector3 pos = GetWorldPosition();
            Quaternion rot = GetWorldRotation();

            // Variables prefixed with AX are Axiom.Math copies of the LL variety.

            Quaternion AXrot = rot;
            AXrot.Normalize();

            Vector3 AXpos = pos;

            // tScale is the offset to derive the vertex based on the scale.
            // it's different for each vertex because we've got to rotate it
            // to get the world position of the vertex to produce the Oriented Bounding Box

            Vector3 tScale = Vector3.Zero;

            Vector3 AXscale = new Vector3(m_shape.Scale.X * 0.5f, m_shape.Scale.Y * 0.5f, m_shape.Scale.Z * 0.5f);           

            //Vector3 pScale = (AXscale) - (AXrot.Inverse() * (AXscale));
            //Vector3 nScale = (AXscale * -1) - (AXrot.Inverse() * (AXscale * -1));

            // rScale is the rotated offset to find a vertex based on the scale and the world rotation.
            Vector3 rScale = new Vector3();

            // Get Vertexes for Faces Stick them into ABCD for each Face
            // Form: Face<vertex>[face] that corresponds to the below diagram
            #region ABCD Face Vertex Map Comment Diagram
            //                   A _________ B
            //                    |         |
            //                    |  4 top  |
            //                    |_________|
            //                   C           D

            //                   A _________ B
            //                    |  Back   |
            //                    |    3    |
            //                    |_________|
            //                   C           D

            //   A _________ B                     B _________ A
            //    |  Left   |                       |  Right  |
            //    |    0    |                       |    2    |
            //    |_________|                       |_________|
            //   C           D                     D           C

            //                   A _________ B
            //                    |  Front  |
            //                    |    1    |
            //                    |_________|
            //                   C           D

            //                   C _________ D
            //                    |         |
            //                    |  5 bot  |
            //                    |_________|
            //                   A           B
            #endregion

            #region Plane Decomposition of Oriented Bounding Box
            tScale = new Vector3(AXscale.X, -AXscale.Y, AXscale.Z);
            rScale = tScale * AXrot;
            vertexes[0] = (new Vector3((pos.X + rScale.X), (pos.Y + rScale.Y), (pos.Z + rScale.Z)));
               // vertexes[0].X = pos.X + vertexes[0].X;
            //vertexes[0].Y = pos.Y + vertexes[0].Y;
            //vertexes[0].Z = pos.Z + vertexes[0].Z;

            FaceA[0] = vertexes[0];
            FaceB[3] = vertexes[0];
            FaceA[4] = vertexes[0];

            tScale = AXscale;
            rScale = tScale * AXrot;
            vertexes[1] = (new Vector3((pos.X + rScale.X), (pos.Y + rScale.Y), (pos.Z + rScale.Z)));

               // vertexes[1].X = pos.X + vertexes[1].X;
               // vertexes[1].Y = pos.Y + vertexes[1].Y;
            //vertexes[1].Z = pos.Z + vertexes[1].Z;

            FaceB[0] = vertexes[1];
            FaceA[1] = vertexes[1];
            FaceC[4] = vertexes[1];

            tScale = new Vector3(AXscale.X, -AXscale.Y, -AXscale.Z);
            rScale = tScale * AXrot;

            vertexes[2] = (new Vector3((pos.X + rScale.X), (pos.Y + rScale.Y), (pos.Z + rScale.Z)));

            //vertexes[2].X = pos.X + vertexes[2].X;
            //vertexes[2].Y = pos.Y + vertexes[2].Y;
            //vertexes[2].Z = pos.Z + vertexes[2].Z;

            FaceC[0] = vertexes[2];
            FaceD[3] = vertexes[2];
            FaceC[5] = vertexes[2];

            tScale = new Vector3(AXscale.X, AXscale.Y, -AXscale.Z);
            rScale = tScale * AXrot;
            vertexes[3] = (new Vector3((pos.X + rScale.X), (pos.Y + rScale.Y), (pos.Z + rScale.Z)));

            //vertexes[3].X = pos.X + vertexes[3].X;
               // vertexes[3].Y = pos.Y + vertexes[3].Y;
               // vertexes[3].Z = pos.Z + vertexes[3].Z;

            FaceD[0] = vertexes[3];
            FaceC[1] = vertexes[3];
            FaceA[5] = vertexes[3];

            tScale = new Vector3(-AXscale.X, AXscale.Y, AXscale.Z);
            rScale = tScale * AXrot;
            vertexes[4] = (new Vector3((pos.X + rScale.X), (pos.Y + rScale.Y), (pos.Z + rScale.Z)));

               // vertexes[4].X = pos.X + vertexes[4].X;
               // vertexes[4].Y = pos.Y + vertexes[4].Y;
               // vertexes[4].Z = pos.Z + vertexes[4].Z;

            FaceB[1] = vertexes[4];
            FaceA[2] = vertexes[4];
            FaceD[4] = vertexes[4];

            tScale = new Vector3(-AXscale.X, AXscale.Y, -AXscale.Z);
            rScale = tScale * AXrot;
            vertexes[5] = (new Vector3((pos.X + rScale.X), (pos.Y + rScale.Y), (pos.Z + rScale.Z)));

               // vertexes[5].X = pos.X + vertexes[5].X;
               // vertexes[5].Y = pos.Y + vertexes[5].Y;
               // vertexes[5].Z = pos.Z + vertexes[5].Z;

            FaceD[1] = vertexes[5];
            FaceC[2] = vertexes[5];
            FaceB[5] = vertexes[5];

            tScale = new Vector3(-AXscale.X, -AXscale.Y, AXscale.Z);
            rScale = tScale * AXrot;
            vertexes[6] = (new Vector3((pos.X + rScale.X), (pos.Y + rScale.Y), (pos.Z + rScale.Z)));

               // vertexes[6].X = pos.X + vertexes[6].X;
               // vertexes[6].Y = pos.Y + vertexes[6].Y;
               // vertexes[6].Z = pos.Z + vertexes[6].Z;

            FaceB[2] = vertexes[6];
            FaceA[3] = vertexes[6];
            FaceB[4] = vertexes[6];

            tScale = new Vector3(-AXscale.X, -AXscale.Y, -AXscale.Z);
            rScale = tScale * AXrot;
            vertexes[7] = (new Vector3((pos.X + rScale.X), (pos.Y + rScale.Y), (pos.Z + rScale.Z)));

               // vertexes[7].X = pos.X + vertexes[7].X;
               // vertexes[7].Y = pos.Y + vertexes[7].Y;
               // vertexes[7].Z = pos.Z + vertexes[7].Z;

            FaceD[2] = vertexes[7];
            FaceC[3] = vertexes[7];
            FaceD[5] = vertexes[7];
            #endregion

            // Get our plane normals
            for (int i = 0; i < 6; i++)
            {
                //m_log.Info("[FACECALCULATION]: FaceA[" + i + "]=" + FaceA[i] + " FaceB[" + i + "]=" + FaceB[i] + " FaceC[" + i + "]=" + FaceC[i] + " FaceD[" + i + "]=" + FaceD[i]);

                // Our Plane direction
                AmBa = FaceA[i] - FaceB[i];
                AmBb = FaceB[i] - FaceC[i];

                cross = Vector3.Cross(AmBb, AmBa);

                // normalize the cross product to get the normal.
                normals[i] = cross / cross.Length();

                //m_log.Info("[NORMALS]: normals[ " + i + "]" + normals[i].ToString());
                //distance[i] = (normals[i].X * AmBa.X + normals[i].Y * AmBa.Y + normals[i].Z * AmBa.Z) * -1;
            }

            EntityIntersection result = new EntityIntersection();

            result.distance = 1024;
            float c = 0;
            float a = 0;
            float d = 0;
            Vector3 q = new Vector3();

            #region OBB Version 2 Experiment
            //float fmin = 999999;
            //float fmax = -999999;
            //float s = 0;

            //for (int i=0;i<6;i++)
            //{
                //s = iray.Direction.Dot(normals[i]);
                //d = normals[i].Dot(FaceB[i]);

                //if (s == 0)
                //{
                    //if (iray.Origin.Dot(normals[i]) > d)
                    //{
                        //return result;
                    //}
                   // else
                    //{
                        //continue;
                    //}
                //}
                //a = (d - iray.Origin.Dot(normals[i])) / s;
                //if (iray.Direction.Dot(normals[i]) < 0)
                //{
                    //if (a > fmax)
                    //{
                        //if (a > fmin)
                        //{
                            //return result;
                        //}
                        //fmax = a;
                    //}

                //}
                //else
                //{
                    //if (a < fmin)
                    //{
                        //if (a < 0 || a < fmax)
                        //{
                            //return result;
                        //}
                        //fmin = a;
                    //}
                //}
            //}
            //if (fmax > 0)
            //    a= fmax;
            //else
               //     a=fmin;

            //q = iray.Origin + a * iray.Direction;
            #endregion

            // Loop over faces (6 of them)
            for (int i = 0; i < 6; i++)
            {
                AmBa = FaceA[i] - FaceB[i];
                AmBb = FaceB[i] - FaceC[i];
                d = Vector3.Dot(normals[i], FaceB[i]);

                //if (faceCenters)
                //{
                //    c = normals[i].Dot(normals[i]);
                //}
                //else
                //{
                c = Vector3.Dot(iray.Direction, normals[i]);
                //}
                if (c == 0)
                    continue;

                a = (d - Vector3.Dot(iray.Origin, normals[i])) / c;

                if (a < 0)
                    continue;

                // If the normal is pointing outside the object
                if (Vector3.Dot(iray.Direction, normals[i]) < 0 || !frontFacesOnly)
                {
                    //if (faceCenters)
                    //{   //(FaceA[i] + FaceB[i] + FaceC[1] + FaceD[i]) / 4f;
                    //    q =  iray.Origin + a * normals[i];
                    //}
                    //else
                    //{
                        q = iray.Origin + iray.Direction * a;
                    //}

                    float distance2 = (float)GetDistanceTo(q, AXpos);
                    // Is this the closest hit to the object's origin?
                    //if (faceCenters)
                    //{
                    //    distance2 = (float)GetDistanceTo(q, iray.Origin);
                    //}

                    if (distance2 < result.distance)
                    {
                        result.distance = distance2;
                        result.HitTF = true;
                        result.ipoint = q;
                        //m_log.Info("[FACE]:" + i.ToString());
                        //m_log.Info("[POINT]: " + q.ToString());
                        //m_log.Info("[DIST]: " + distance2.ToString());
                        if (faceCenters)
                        {
                            result.normal = AAfacenormals[i] * AXrot;

                            Vector3 scaleComponent = AAfacenormals[i];
                            float ScaleOffset = 0.5f;
                            if (scaleComponent.X != 0) ScaleOffset = AXscale.X;
                            if (scaleComponent.Y != 0) ScaleOffset = AXscale.Y;
                            if (scaleComponent.Z != 0) ScaleOffset = AXscale.Z;
                            ScaleOffset = Math.Abs(ScaleOffset);
                            Vector3 offset = result.normal * ScaleOffset;
                            result.ipoint = AXpos + offset;

                            ///pos = (intersectionpoint + offset);
                        }
                        else
                        {
                            result.normal = normals[i];
                        }
                        result.AAfaceNormal = AAfacenormals[i];
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Serialize this part to xml.
        /// </summary>
        /// <param name="xmlWriter"></param>
        public void ToXml(XmlTextWriter xmlWriter)
        {
            SceneObjectSerializer.SOPToXml2(xmlWriter, this, new Dictionary<string, object>());
        }

        public void TriggerScriptChangedEvent(Changed val)
        {
            if (m_parentGroup != null && m_parentGroup.Scene != null)
                m_parentGroup.Scene.EventManager.TriggerOnScriptChangedEvent(this, (uint)val);
        }

        public void TrimPermissions()
        {
            _baseMask &= (uint)PermissionMask.All;
            _ownerMask &= (uint)PermissionMask.All;
            _groupMask &= (uint)PermissionMask.All;
            _everyoneMask &= (uint)PermissionMask.All;
            _nextOwnerMask &= (uint)PermissionMask.All;
        }

        public void Undo()
        {
            lock (m_undo)
            {
                if (m_undo.Count > 0)
                {
                    m_redo.Push(new UndoState(this));
                    UndoState goback = m_undo.Pop();
                    if (goback != null)
                    {
                        goback.PlaybackState(this);
                    }
                }
            }
        }

        public void Redo()
        {
            lock (m_redo)
            {
                if (m_redo.Count > 0)
                {
                    UndoState nUndo = new UndoState(this);
                    m_undo.Push(nUndo);

                    UndoState gofwd = m_redo.Pop();
                    if (gofwd != null)
                        gofwd.PlayfwdState(this);
                }
            }
        }

        public void UpdateExtraParam(ushort type, bool inUse, byte[] data)
        {
            m_shape.ReadInUpdateExtraParam(type, inUse, data);

            if (type == 0x30)
            {
                if (m_shape.SculptEntry && m_shape.SculptTexture != UUID.Zero)
                {
                    m_parentGroup.Scene.AssetService.Get(m_shape.SculptTexture.ToString(), this, AssetReceived);
                }
            }

            ParentGroup.HasGroupChanged = true;
            ScheduleUpdate(PrimUpdateFlags.Shape);
        }

        public void UpdateGroupPosition(Vector3 pos)
        {
            if ((pos.X != GroupPosition.X) ||
                (pos.Y != GroupPosition.Y) ||
                (pos.Z != GroupPosition.Z))
            {
//                Vector3 newPos = new Vector3(pos.X, pos.Y, pos.Z);
                FixGroupPosition(pos,false);
                ScheduleTerseUpdate();
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="pos"></param>
        public void UpdateOffSet(Vector3 pos)
        {
            if ((pos.X != OffsetPosition.X) ||
                (pos.Y != OffsetPosition.Y) ||
                (pos.Z != OffsetPosition.Z))
            {
                Vector3 newPos = new Vector3(pos.X, pos.Y, pos.Z);

                if (ParentGroup.RootPart.GetStatusSandbox())
                {
                    if (Util.GetDistanceTo(ParentGroup.RootPart.StatusSandboxPos, newPos) > 10)
                    {
                        ParentGroup.RootPart.ScriptSetPhysicsStatus(false);
                        newPos = OffsetPosition;
                        IChatModule chatModule = ParentGroup.Scene.RequestModuleInterface<IChatModule>();
                        if (chatModule != null)
                            chatModule.SimChat("Hit Sandbox Limit", ChatTypeEnum.DebugChannel, 0x7FFFFFFF,
                                ParentGroup.RootPart.AbsolutePosition, Name, UUID, false, ParentGroup.Scene);
                    }
                }

                FixOffsetPosition(newPos,true);
                ScheduleTerseUpdate();
            }
        }

        public void UpdatePermissions(UUID AgentID, byte field, uint localID, uint mask, byte addRemTF)
        {
            bool set = addRemTF == 1;
            bool god = m_parentGroup.Scene.Permissions.IsGod(AgentID);

            uint baseMask = _baseMask;
            if (god)
                baseMask = 0x7ffffff0;

            // Are we the owner?
            if (m_parentGroup.Scene.Permissions.CanEditObject(this.UUID, AgentID))
            {
                uint exportPermission = (1 << 30);
                if ((mask & exportPermission) == exportPermission)
                {
                    //Only the creator can set export permissions
                    if (CreatorID != AgentID)
                        mask &= exportPermission;
                }

                switch (field)
                {
                    case 1:
                        if (god)
                        {
                            _baseMask = ApplyMask(_baseMask, set, mask);
                            Inventory.ApplyGodPermissions(_baseMask);
                        }

                        break;
                    case 2:
                        _ownerMask = ApplyMask(_ownerMask, set, mask) &
                                baseMask;
                        break;
                    case 4:
                        _groupMask = ApplyMask(_groupMask, set, mask) &
                                baseMask;
                        break;
                    case 8:
                        _everyoneMask = ApplyMask(_everyoneMask, set, mask) &
                                baseMask;
                        break;
                    case 16:
                        _nextOwnerMask = ApplyMask(_nextOwnerMask, set, mask) &
                                baseMask;
                        // Prevent the client from creating no mod, no copy
                        // objects
                        if ((_nextOwnerMask & (uint)PermissionMask.Copy) == 0)
                            _nextOwnerMask |= (uint)PermissionMask.Transfer;

                        _nextOwnerMask |= (uint)PermissionMask.Move;

                        break;
                }
                ParentGroup.ScheduleGroupUpdate(PrimUpdateFlags.PrimFlags);

                SendObjectPropertiesToClient(AgentID);

            }
        }

        public bool IsHingeJoint()
        {
            // For now, we use the NINJA naming scheme for identifying joints.
            // In the future, we can support other joint specification schemes such as a 
            // custom checkbox in the viewer GUI.
            if (m_parentGroup.Scene.SceneGraph.PhysicsScene.SupportsNINJAJoints)
            {
                string hingeString = "hingejoint";
                return (Name.Length >= hingeString.Length && Name.Substring(0, hingeString.Length) == hingeString);
            }
            else
            {
                return false;
            }
        }

        public bool IsBallJoint()
        {
            // For now, we use the NINJA naming scheme for identifying joints.
            // In the future, we can support other joint specification schemes such as a 
            // custom checkbox in the viewer GUI.
            if (m_parentGroup.Scene.SceneGraph.PhysicsScene.SupportsNINJAJoints)
            {
                string ballString = "balljoint";
                return (Name.Length >= ballString.Length && Name.Substring(0, ballString.Length) == ballString);
            }
            else
            {
                return false;
            }
        }

        public bool IsJoint()
        {
            // For now, we use the NINJA naming scheme for identifying joints.
            // In the future, we can support other joint specification schemes such as a 
            // custom checkbox in the viewer GUI.
            if (m_parentGroup.Scene.SceneGraph.PhysicsScene.SupportsNINJAJoints)
            {
                return IsHingeJoint() || IsBallJoint();
            }
            else
            {
                return false;
            }
        }

        public void UpdatePrimFlags(bool UsePhysics, bool IsTemporary, bool IsPhantom, bool IsVD)
        {
            bool wasUsingPhysics = ((Flags & PrimFlags.Physics) != 0);
            bool wasTemporary = ((Flags & PrimFlags.TemporaryOnRez) != 0);
            bool wasPhantom = ((Flags & PrimFlags.Phantom) != 0);
            bool wasVD = VolumeDetectActive;

            if ((UsePhysics == wasUsingPhysics) && (wasTemporary == IsTemporary) && (wasPhantom == IsPhantom) && (IsVD==wasVD))
            {
                return;
            }

            // Special cases for VD. VD can only be called from a script 
            // and can't be combined with changes to other states. So we can rely
            // that...
            // ... if VD is changed, all others are not.
            // ... if one of the others is changed, VD is not.
            if (IsVD) // VD is active, special logic applies
            {
                // State machine logic for VolumeDetect
                // More logic below
                bool phanReset = (IsPhantom != wasPhantom) && !IsPhantom;

                if (phanReset) // Phantom changes from on to off switch VD off too
                {
                    IsVD = false;               // Switch it of for the course of this routine
                    VolumeDetectActive = false; // and also permanently
                    if (PhysActor != null)
                        PhysActor.SetVolumeDetect(0);   // Let physics know about it too
                }
                else
                {
                    IsPhantom = false;
                    // If volumedetect is active we don't want phantom to be applied.
                    // If this is a new call to VD out of the state "phantom"
                    // this will also cause the prim to be visible to physics
                }

            }

            if (UsePhysics && IsJoint())
            {
                IsPhantom = true;
            }

            if (UsePhysics)
            {
                AddFlag(PrimFlags.Physics);
                if (!wasUsingPhysics)
                {
                    DoPhysicsPropertyUpdate(UsePhysics, false);
                    if (m_parentGroup != null)
                    {
                        if (!m_parentGroup.IsDeleted)
                        {
                            if (LocalId == m_parentGroup.RootPart.LocalId)
                            {
                                m_parentGroup.CheckSculptAndLoad();
                            }
                        }
                    }
                }
                if (PhysActor != null)
                {
                    PhysActor.OnCollisionUpdate += PhysicsCollision;
                    PhysActor.SubscribeEvents(1000);
                }
            }
            else
            {
                RemFlag(PrimFlags.Physics);
                if (wasUsingPhysics)
                {
                    DoPhysicsPropertyUpdate(UsePhysics, false);
                }
                if (PhysActor != null)
                {
                    PhysActor.UnSubscribeEvents();
                    PhysActor.OnCollisionUpdate -= PhysicsCollision;
                }
            }


            if (IsPhantom || IsAttachment || (Shape.PathCurve == (byte)Extrusion.Flexible)) // note: this may have been changed above in the case of joints
            {
                AddFlag(PrimFlags.Phantom);
                if (PhysActor != null)
                {
                    m_parentGroup.Scene.SceneGraph.PhysicsScene.RemovePrim(PhysActor);
                    /// that's not wholesome.  Had to make Scene public
                    PhysActor = null;
                }
            }
            else // Not phantom
            {
                RemFlag(PrimFlags.Phantom);

                PhysicsActor pa = PhysActor;
                if (pa == null)
                {
                    // It's not phantom anymore. So make sure the physics engine get's knowledge of it
                    Vector3 tmp = GetWorldPosition();
                    Quaternion qtmp = GetWorldRotation();
                    PhysActor = m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPrimShape(
                        string.Format("{0}/{1}", Name, UUID),
                        Shape,
                        tmp,
                        Scale,
                        qtmp,
                        UsePhysics);

                    pa = PhysActor;
                    if (pa != null)
                    {
                        pa.LocalID = LocalId;
                        DoPhysicsPropertyUpdate(UsePhysics, true);
                        if (m_parentGroup != null)
                        {
                            if (!m_parentGroup.IsDeleted)
                            {
                                if (LocalId == m_parentGroup.RootPart.LocalId)
                                {
                                    m_parentGroup.CheckSculptAndLoad();
                                }
                            }
                        }
                        PhysActor.OnCollisionUpdate += PhysicsCollision;
                        PhysActor.SubscribeEvents(1000);
                    }
                }
                else // it already has a physical representation
                {
                    pa.IsPhysical = UsePhysics;

                    DoPhysicsPropertyUpdate(UsePhysics, false); // Update physical status. If it's phantom this will remove the prim
                    if (m_parentGroup != null)
                    {
                        if (!m_parentGroup.IsDeleted)
                        {
                            if (LocalId == m_parentGroup.RootPart.LocalId)
                            {
                                m_parentGroup.CheckSculptAndLoad();
                            }
                        }
                    }
                }
            }

            if (IsVD && IsVD != this.VolumeDetectActive)
            {
                // If the above logic worked (this is urgent candidate to unit tests!)
                // we now have a physicsactor.
                // Defensive programming calls for a check here.
                // Better would be throwing an exception that could be catched by a unit test as the internal 
                // logic should make sure, this Physactor is always here.
                if (this.PhysActor != null)
                {
                    PhysActor.SetVolumeDetect(1);
                    AddFlag(PrimFlags.Phantom); // We set this flag also if VD is active
                    this.VolumeDetectActive = true;
                }
            }
            else
            {
                if (IsVD != this.VolumeDetectActive)
                {
                    // Remove VolumeDetect in any case. Note, it's safe to call SetVolumeDetect as often as you like
                    // (mumbles, well, at least if you have infinte CPU powers :-))
                    PhysicsActor pa = this.PhysActor;
                    if (pa != null)
                    {
                        PhysActor.SetVolumeDetect(0);
                    }
                    this.VolumeDetectActive = false;
                }
            }


            if (IsTemporary)
            {
                AddFlag(PrimFlags.TemporaryOnRez);
            }
            else
            {
                RemFlag(PrimFlags.TemporaryOnRez);
            }
            //            m_log.Debug("Update:  PHY:" + UsePhysics.ToString() + ", T:" + IsTemporary.ToString() + ", PHA:" + IsPhantom.ToString() + " S:" + CastsShadows.ToString());

            ParentGroup.HasGroupChanged = true;
            ScheduleUpdate(PrimUpdateFlags.PrimFlags);
        }

        public void UpdateRotation(Quaternion rot)
        {
            if ((rot.X != RotationOffset.X) ||
                (rot.Y != RotationOffset.Y) ||
                (rot.Z != RotationOffset.Z) ||
                (rot.W != RotationOffset.W))
            {
                RotationOffset = rot;
                ParentGroup.HasGroupChanged = true;
                ScheduleTerseUpdate();
            }
        }

        /// <summary>
        /// Update the shape of this part.
        /// </summary>
        /// <param name="shapeBlock"></param>
        public void UpdateShape(ObjectShapePacket.ObjectDataBlock shapeBlock)
        {
            IOpenRegionSettingsModule module = ParentGroup.Scene.RequestModuleInterface<IOpenRegionSettingsModule>();
            if (module != null)
            {
                if (shapeBlock.ProfileHollow > module.MaximumHollowSize * 500 &&
                    module.MaximumHollowSize != -1) //This is so that it works correctly, since the packet sends (N * 500)
                {
                    shapeBlock.ProfileHollow = (ushort)(module.MaximumHollowSize * 500);
                }
                if (shapeBlock.PathScaleY > (200 - (module.MinimumHoleSize * 100)) &&
                    module.MinimumHoleSize != -1 && shapeBlock.PathCurve == 32) //This is how the packet is set up... so this is how we check for it...
                {
                    shapeBlock.PathScaleY = Convert.ToByte((200 - (module.MinimumHoleSize * 100)));
                }
            }

            m_shape.PathBegin = shapeBlock.PathBegin;
            m_shape.PathEnd = shapeBlock.PathEnd;
            m_shape.PathScaleX = shapeBlock.PathScaleX;
            m_shape.PathScaleY = shapeBlock.PathScaleY;
            m_shape.PathShearX = shapeBlock.PathShearX;
            m_shape.PathShearY = shapeBlock.PathShearY;
            m_shape.PathSkew = shapeBlock.PathSkew;
            m_shape.ProfileBegin = shapeBlock.ProfileBegin;
            m_shape.ProfileEnd = shapeBlock.ProfileEnd;
            m_shape.PathCurve = shapeBlock.PathCurve;
            m_shape.ProfileCurve = shapeBlock.ProfileCurve;

            m_shape.ProfileHollow = shapeBlock.ProfileHollow;
            m_shape.PathRadiusOffset = shapeBlock.PathRadiusOffset;
            m_shape.PathRevolutions = shapeBlock.PathRevolutions;
            m_shape.PathTaperX = shapeBlock.PathTaperX;
            m_shape.PathTaperY = shapeBlock.PathTaperY;
            m_shape.PathTwist = shapeBlock.PathTwist;
            m_shape.PathTwistBegin = shapeBlock.PathTwistBegin;

            Shape = m_shape;

            if (PhysActor != null)
            {
                PhysActor.Shape = m_shape;
                m_parentGroup.Scene.SceneGraph.PhysicsScene.AddPhysicsActorTaint(PhysActor);
            }

            // This is what makes vehicle trailers work
            // A script in a child prim re-issues
            // llSetPrimitiveParams(PRIM_TYPE) every few seconds. That
            // prevents autoreturn. This also works in SL.
            if (ParentGroup.RootPart != this)
                ParentGroup.RootPart.Rezzed = DateTime.UtcNow;

            ParentGroup.HasGroupChanged = true;
            ScheduleUpdate(PrimUpdateFlags.Shape);
        }

        /// <summary>
        /// Update the textures on the part.
        /// </summary>
        /// Added to handle bug in libsecondlife's TextureEntry.ToBytes()
        /// not handling RGBA properly. Cycles through, and "fixes" the color
        /// info
        /// <param name="tex"></param>
        public void UpdateTexture(Primitive.TextureEntry tex)
        {
            //Color4 tmpcolor;
            //for (uint i = 0; i < 32; i++)
            //{
            //    if (tex.FaceTextures[i] != null)
            //    {
            //        tmpcolor = tex.GetFace((uint) i).RGBA;
            //        tmpcolor.A = tmpcolor.A*255;
            //        tmpcolor.R = tmpcolor.R*255;
            //        tmpcolor.G = tmpcolor.G*255;
            //        tmpcolor.B = tmpcolor.B*255;
            //        tex.FaceTextures[i].RGBA = tmpcolor;
            //    }
            //}
            //tmpcolor = tex.DefaultTexture.RGBA;
            //tmpcolor.A = tmpcolor.A*255;
            //tmpcolor.R = tmpcolor.R*255;
            //tmpcolor.G = tmpcolor.G*255;
            //tmpcolor.B = tmpcolor.B*255;
            //tex.DefaultTexture.RGBA = tmpcolor;
            UpdateTextureEntry(tex.GetBytes());
        }

        /// <summary>
        /// Update the texture entry for this part.
        /// </summary>
        /// <param name="textureEntry"></param>
        public void UpdateTextureEntry(byte[] textureEntry)
        {
            Primitive.TextureEntry oldEntry = m_shape.Textures;
            m_shape.TextureEntry = textureEntry;
            if (m_shape.Textures.DefaultTexture.RGBA.A != oldEntry.DefaultTexture.RGBA.A ||
                m_shape.Textures.DefaultTexture.RGBA.R != oldEntry.DefaultTexture.RGBA.R ||
                m_shape.Textures.DefaultTexture.RGBA.G != oldEntry.DefaultTexture.RGBA.G ||
                m_shape.Textures.DefaultTexture.RGBA.B != oldEntry.DefaultTexture.RGBA.B)
            {
                TriggerScriptChangedEvent(Changed.COLOR);
            }
            else
            {
                for (int i = 0; i < 6; i++)
                {
                    if (m_shape != null && m_shape.Textures != null && 
                        m_shape.Textures.FaceTextures[i] != null &&
                        oldEntry != null && oldEntry.FaceTextures[i] != null)
                    {
                        if (m_shape.Textures.FaceTextures[i].RGBA.A != oldEntry.FaceTextures[i].RGBA.A ||
                            m_shape.Textures.FaceTextures[i].RGBA.R != oldEntry.FaceTextures[i].RGBA.R ||
                            m_shape.Textures.FaceTextures[i].RGBA.G != oldEntry.FaceTextures[i].RGBA.G ||
                            m_shape.Textures.FaceTextures[i].RGBA.B != oldEntry.FaceTextures[i].RGBA.B)
                        {
                            TriggerScriptChangedEvent(Changed.COLOR);
                        }
                        if (m_shape.Textures.FaceTextures[i].TextureID != oldEntry.FaceTextures[i].TextureID)
                        {
                            TriggerScriptChangedEvent(Changed.TEXTURE);
                        }
                    }
                }
            }

            ParentGroup.HasGroupChanged = true;
            ScheduleUpdate(PrimUpdateFlags.FullUpdate);
        }

        public void aggregateScriptEvents()
        {
            AggregateScriptEvents = 0;

            // Aggregate script events
            lock (m_scriptEvents)
            {
                foreach (scriptEvents s in m_scriptEvents.Values)
                {
                    AggregateScriptEvents |= s;
                }
            }

            uint objectflagupdate = 0;

            if (
                ((AggregateScriptEvents & scriptEvents.touch) != 0) ||
                ((AggregateScriptEvents & scriptEvents.touch_end) != 0) ||
                ((AggregateScriptEvents & scriptEvents.touch_start) != 0)
                )
            {
                objectflagupdate |= (uint) PrimFlags.Touch;
            }

            if ((AggregateScriptEvents & scriptEvents.money) != 0)
            {
                objectflagupdate |= (uint) PrimFlags.Money;
            }

            if (AllowedDrop)
            {
                objectflagupdate |= (uint) PrimFlags.AllowInventoryDrop;
            }

            // subscribe to physics updates.
            if (PhysActor != null)
            {
                PhysActor.OnCollisionUpdate += PhysicsCollision;
                PhysActor.SubscribeEvents(1000);
            }

            if (m_parentGroup == null)
            {
//                m_log.DebugFormat(
//                    "[SCENE OBJECT PART]: Scheduling part {0} {1} for full update in aggregateScriptEvents() since m_parentGroup == null", Name, LocalId);
                ScheduleUpdate(PrimUpdateFlags.FullUpdate);
                return;
            }

            LocalFlags=(PrimFlags)objectflagupdate;

            if (m_parentGroup != null && m_parentGroup.RootPart == this)
            {
                m_parentGroup.aggregateScriptEvents();
            }
            else
            {
//                m_log.DebugFormat(
//                    "[SCENE OBJECT PART]: Scheduling part {0} {1} for full update in aggregateScriptEvents()", Name, LocalId);
                ScheduleUpdate(PrimUpdateFlags.PrimFlags);
            }
        }

        public int registerTargetWaypoint(Vector3 target, float tolerance)
        {
            if (m_parentGroup != null)
            {
                return m_parentGroup.registerTargetWaypoint(target, tolerance);
            }
            return 0;
        }

        public void unregisterTargetWaypoint(int handle)
        {
            if (m_parentGroup != null)
            {
                m_parentGroup.unregisterTargetWaypoint(handle);
            }
        }

        public int registerRotTargetWaypoint(Quaternion target, float tolerance)
        {
            if (m_parentGroup != null)
            {
                return m_parentGroup.registerRotTargetWaypoint(target, tolerance);
            }
            return 0;
        }

        public void unregisterRotTargetWaypoint(int handle)
        {
            if (m_parentGroup != null)
            {
                m_parentGroup.unregisterRotTargetWaypoint(handle);
            }
        }
        
        public override string ToString()
        {
            return String.Format("{0} {1} linkNum {3} (parent {2}))", Name, UUID, ParentGroup, LinkNum);
        }

        #endregion Public Methods

        public void ApplyNextOwnerPermissions()
        {
            _baseMask &= _nextOwnerMask;
            _ownerMask &= _nextOwnerMask;
            _everyoneMask &= _nextOwnerMask;

            Inventory.ApplyNextOwnerPermissions();
        }

        public void UpdateLookAt()
        {
            try
            {
                if (PhysActor == null)
                {
                    //Non physical PID movement
                    // Has to be phantom and physical
                    if (PIDActive && ((Flags & PrimFlags.Phantom) != 0) &&
                        ((Flags & PrimFlags.Physics) != 0))
                    {
                        Vector3 _target_velocity =
                                new Vector3(
                                    (float)(PIDTarget.X - m_initialPIDLocation.X) * (PIDTau * ParentGroup.Scene.SceneGraph.PhysicsScene.StepTime * 0.75f),
                                    (float)(PIDTarget.Y - m_initialPIDLocation.Y) * (PIDTau * ParentGroup.Scene.SceneGraph.PhysicsScene.StepTime * 0.75f),
                                    (float)(PIDTarget.Z - m_initialPIDLocation.Z) * (PIDTau * ParentGroup.Scene.SceneGraph.PhysicsScene.StepTime * 0.75f)
                                    );
                        if (PIDTarget.ApproxEquals(AbsolutePosition, 0.1f))
                        {
                            ParentGroup.SetAbsolutePosition(false, PIDTarget + _target_velocity);
                            this.ScheduleTerseUpdate();
                            //End the movement
                            SetMoveToTarget(false, Vector3.Zero, 0);
                        }
                        else
                        {
                            ParentGroup.SetAbsolutePosition(false, AbsolutePosition + _target_velocity);
                            this.ScheduleTerseUpdate();
                        }
                    }
                }
                else
                {
                    //Reset the PID attributes

                }
                if (APIDTarget != Quaternion.Identity)
                {
                    if (Single.IsNaN(APIDTarget.W) == true)
                    {
                        APIDTarget = Quaternion.Identity;
                        return;
                    }
                    Quaternion rot = RotationOffset;
                    Quaternion dir = (rot - APIDTarget);
                    float speed = ((APIDStrength / APIDDamp) * (float)(Math.PI / 180.0f));
                    if (dir.Z > speed)
                    {
                        rot.Z -= speed;
                    }
                    if (dir.Z < -speed)
                    {
                        rot.Z += speed;
                    }
                    rot.Normalize();
                    UpdateRotation(rot);
                }
            }
            catch (Exception ex)
            {
                m_log.Error("[Physics] " + ex);
            }
        }

        public Color4 GetTextColor()
        {
            Color color = Color;
            return new Color4(color.R, color.G, color.B, (byte)(0xFF - color.A));
        }

        public void SetSoundQueueing(int queue)
        {
            UseSoundQueue = queue;
        }

        public void SetConeOfSilence(double radius)
        {
            ISoundModule module = m_parentGroup.Scene.RequestModuleInterface<ISoundModule>();
            //TODO: Save SetConeOfSilence
            if (module != null)
            {
                if (radius != 0)
                    module.AddConeOfSilence(UUID, AbsolutePosition, radius);
                else
                    module.RemoveConeOfSilence(UUID);
            }
        }

        internal void TriggerScriptMovingStartEvent()
        {
            if (m_parentGroup != null && m_parentGroup.Scene != null)
                m_parentGroup.Scene.EventManager.TriggerOnScriptMovingStartEvent(this);
        }

        internal void TriggerScriptMovingEndEvent()
        {
            if (m_parentGroup != null && m_parentGroup.Scene != null)
                m_parentGroup.Scene.EventManager.TriggerOnScriptMovingEndEvent(this);
        }
    }
}
