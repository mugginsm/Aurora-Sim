/* Copyright (c) Contributors, http://opensimulator.org/
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

/*
 * Revised August 26 2009 by Kitto Flora. ODEDynamics.cs replaces
 * ODEVehicleSettings.cs. It and ODEPrim.cs are re-organised:
 * ODEPrim.cs contains methods dealing with Prim editing, Prim
 * characteristics and Kinetic motion.
 * ODEDynamics.cs contains methods dealing with Prim Physical motion
 * (dynamics) and the associated settings. Old Linear and angular
 * motors for dynamic motion have been replace with  MoveLinear()
 * and MoveAngular(); 'Physical' is used only to switch ODE dynamic 
 * simualtion on/off; VEHICAL_TYPE_NONE/VEHICAL_TYPE_<other> is to
 * switch between 'VEHICLE' parameter use and general dynamics
 * settings use.
 */
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using log4net;
using OpenMetaverse;
using Ode.NET;
using OpenSim.Framework;
using OpenSim.Region.Physics.Manager;
using changes = Aurora.Physics.AuroraOpenDynamicsEngine.AuroraODEPhysicsScene.changes;

namespace Aurora.Physics.AuroraOpenDynamicsEngine
{
    /// <summary>
    /// Various properties that ODE uses for AMotors but isn't exposed in ODE.NET so we must define them ourselves.
    /// </summary>

    public class AuroraODEPrim : PhysicsActor
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Vector3 _position;
        private Vector3 showposition; // a temp hack for now rest of code expects position to be changed imediatly
                                      // and now we do it delayed, so fake we changed it
        bool fakepos = false;                 // control the use of above
        private Vector3 _velocity;
        private Vector3 _torque = Vector3.Zero;
        private Vector3 m_lastVelocity;
        private Vector3 m_lastRotationalVelocity;
        private Vector3 m_lastposition;
        //private Vector3 m_lastSignificantPosition;
        private Quaternion m_lastorientation = new Quaternion();
        private Vector3 m_rotationalVelocity;
        private Vector3 _size;
        private Vector3 _acceleration;
        private Quaternion _orientation;
        private Quaternion showorientation; // tmp hack see showposition
        bool fakeori = false;                 // control the use of above

        public Vector3 m_angularlock = Vector3.One;
        public IntPtr Amotor = IntPtr.Zero;
        public d.Matrix3 AmotorRotation = new d.Matrix3();

        private Vector3 m_PIDTarget;
        private float m_PIDTau;
        private float PID_D = 35f;
        private float PID_G = 25f;
        private bool m_usePID;

        // KF: These next 7 params apply to llSetHoverHeight(float height, integer water, float tau),
        // and are for non-VEHICLES only.

        private float m_PIDHoverHeight;
        private float m_PIDHoverTau;
        private bool m_useHoverPID;
        private PIDHoverType m_PIDHoverType = PIDHoverType.Ground;
        private float m_targetHoverHeight;
        private float m_groundHeight;
        private float m_waterHeight;
        private float m_buoyancy;                //KF: m_buoyancy should be set by llSetBuoyancy() for non-vehicle. 

        // private float m_tensor = 5f;
        private int body_autodisable_frames = 20;


        private const CollisionCategories m_default_collisionFlags = (CollisionCategories.Geom
                                                        | CollisionCategories.Space
                                                        | CollisionCategories.Body
                                                        | CollisionCategories.Character
                                                        );
        private bool m_collidesLand = true;
        private bool m_collidesWater;
        public bool m_returnCollisions;
        private bool testRealGravity = false;

        // Default we're a Geometry
        private CollisionCategories m_collisionCategories = (CollisionCategories.Geom);

        // Default, Collide with Other Geometries, spaces and Bodies
        private CollisionCategories m_collisionFlags = m_default_collisionFlags;
       
        public bool m_disabled;
        //This disables the prim so that it cannot do much anything at all
        public bool m_frozen = false;

        public uint m_localID;

        //public GCHandle gc;
        private CollisionLocker ode;

        private Vector3 m_force;
        private Vector3 m_forceacc;
        private Vector3 m_angularforceacc;

        private IMesh _mesh;
        private PrimitiveBaseShape _pbs;
        private AuroraODEPhysicsScene _parent_scene;
        public IntPtr m_targetSpace = IntPtr.Zero;
        public IntPtr prim_geom;
        public IntPtr prev_geom;
        public IntPtr _triMeshData;

        private PhysicsActor _parent;

        private List<AuroraODEPrim> childrenPrim = new List<AuroraODEPrim>();

        private bool iscolliding;
        private bool m_isphysical;
        private bool m_isSelected;

        internal bool m_isVolumeDetect; // If true, this prim only detects collisions but doesn't collide actively

        private bool m_throttleUpdates;
        private int throttleCounter;
        //private int _updatesPerThrottledUpdate;
        public int m_interpenetrationcount;
        public float m_collisionscore;
        private int m_crossingfailures;

        public bool outofBounds;
        private float m_density = 10.000006836f; // Aluminum g/cm3;

        public bool _zeroFlag;
        private int m_lastUpdateSent = 0;

        public IntPtr Body = IntPtr.Zero;
        public String m_primName;
        private Vector3 _target_velocity;
        public d.Mass primdMass;
        float primMass; // prim own mass
        float _mass; // prim or object mass
        d.AABB aabb;

        public int m_eventsubscription;
        private CollisionEventUpdate CollisionEventsThisFrame;

        public volatile bool childPrim;

        private AuroraODEDynamics m_vehicle;

        internal int m_material = (int)Material.Wood;

        public AuroraODEPrim(String primName, AuroraODEPhysicsScene parent_scene, Vector3 pos, Vector3 size,
                       Quaternion rotation, IMesh mesh, PrimitiveBaseShape pbs, bool pisPhysical, CollisionLocker dode)
        {
            m_vehicle = new AuroraODEDynamics();
            //gc = GCHandle.Alloc(prim_geom, GCHandleType.Pinned);
            ode = dode;
            if (!pos.IsFinite())
            {
                pos = new Vector3((parent_scene.Region.RegionSizeX * 0.5f), (parent_scene.Region.RegionSizeY * 0.5f),
                    parent_scene.GetTerrainHeightAtXY((parent_scene.Region.RegionSizeX * 0.5f), (parent_scene.Region.RegionSizeY * 0.5f)));
                m_log.Warn("[PHYSICS]: Got nonFinite Object create Position");
            }
            _position = pos;
            fakepos = false;

            PID_D = parent_scene.bodyPIDD;
            PID_G = parent_scene.bodyPIDG;

            // correct for changed timestep
            PID_D /= (parent_scene.ODE_STEPSIZE * 50f); // original ode fps of 50
            PID_G /= (parent_scene.ODE_STEPSIZE * 50f);

            m_density = parent_scene.geomDefaultDensity;
            // m_tensor = parent_scene.bodyMotorJointMaxforceTensor;
            body_autodisable_frames = parent_scene.bodyFramesAutoDisable;


            prim_geom = IntPtr.Zero;
            prev_geom = IntPtr.Zero;

            if (!size.IsFinite())
            {
                size = new Vector3(0.5f, 0.5f, 0.5f);
                m_log.Warn("[PHYSICS]: Got nonFinite Object create Size");
            }

            if (size.X <= 0) size.X = 0.01f;
            if (size.Y <= 0) size.Y = 0.01f;
            if (size.Z <= 0) size.Z = 0.01f;

            _size = size;

            if (!QuaternionIsFinite(rotation))
            {
                rotation = Quaternion.Identity;
                m_log.Warn("[PHYSICS]: Got nonFinite Object create Rotation");
            }

            _orientation = rotation;
            fakeori = false;

            _mesh = mesh;
            _pbs = pbs;

            _parent_scene = parent_scene;
            m_targetSpace = (IntPtr)0;

            if (pos.Z < 0)
                m_isphysical = false;
            else
            {
                m_isphysical = pisPhysical;
                // If we're physical, we need to be in the master space for now.
                // linksets *should* be in a space together..  but are not currently
                if (m_isphysical)
                    m_targetSpace = _parent_scene.space;
            }
            m_primName = primName;

            m_forceacc = Vector3.Zero;
            m_angularforceacc = Vector3.Zero;

            AddChange(changes.Add, null);
        }

        public override int PhysicsActorType
        {
            get { return (int) ActorTypes.Prim; }
            set { return; }
        }

        public override bool SetAlwaysRun
        {
            get { return false; }
            set { return; }
        }

        public override uint LocalID
        {
            set {
                //m_log.Info("[PHYSICS]: Setting TrackerID: " + value);
                m_localID = value; }
        }

        public override bool Grabbed
        {
            set { return; }
        }

        public override bool VolumeDetect
        {
            get { return m_isVolumeDetect; }
        }

        public override bool Selected
        {
            set {
        
            
                // This only makes the object not collidable if the object
                // is physical or the object is modified somehow *IN THE FUTURE*
                // without this, if an avatar selects prim, they can walk right
                // through it while it's selected
                m_collisionscore = 0;
                if ((IsPhysical && !_zeroFlag) || !value)
                {
                AddChange(changes.Selected, (object)value);
                }
                else
                {
                    m_isSelected = value;
                }
                if (m_isSelected) disableBodySoft();
            }
        }

        public void SetGeom(IntPtr geom)
        {
            prev_geom = prim_geom;
            prim_geom = geom;
//Console.WriteLine("SetGeom to " + prim_geom + " for " + m_primName);
            if (prim_geom != IntPtr.Zero)
            {
                d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
                d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
                d.GeomGetAABB(prim_geom, out aabb);
            }

            

            if (childPrim)
            {
                if (_parent != null && _parent is AuroraODEPrim)
                {
                    AuroraODEPrim parent = (AuroraODEPrim)_parent;
//Console.WriteLine("SetGeom calls ChildSetGeom");
                    parent.ChildSetGeom(this);
                }
            }
            //m_log.Warn("Setting Geom to: " + prim_geom);
        }

        

        public void enableBodySoft()
        {
            if (!childPrim)
            {
                if (m_isphysical && Body != IntPtr.Zero)
                {
                    d.BodyEnable(Body);
                    if (m_vehicle.Type != Vehicle.TYPE_NONE)
                        m_vehicle.Enable(Body, this,_parent_scene);
                }

                m_disabled = false;
            }
        }

        public void disableBodySoft()
        {
        if (!childPrim)
            {
            m_disabled = true;
            m_vehicle.Disable(this);
            if (IsPhysical && Body != IntPtr.Zero)
                {
                d.BodyDisable(Body);
                }
            }
        }

        public void MakeBody()
            {
//            d.Vector3 dvtmp;
//            d.Vector3 dbtmp;
            
            d.Mass tmpdmass = new d.Mass { };
            d.Mass objdmass = new d.Mass { };

            d.Matrix3 mat = new d.Matrix3();
            d.Matrix3 mymat = new d.Matrix3();
            d.Quaternion quat = new d.Quaternion();
            d.Quaternion myrot = new d.Quaternion();
            Vector3 rcm;

            if (childPrim)  // child prims don't get own bodies;
                return;

            if (Body != IntPtr.Zero) // who shouldn't have one already ?
                {
                d.BodyDestroy(Body);
                Body = IntPtr.Zero;
                }

            if (!m_isphysical) // only physical things get a body
                return;
            Body = d.BodyCreate(_parent_scene.world);

            calcdMass(); // compute inertia on local frame

            DMassDup(ref primdMass, out objdmass);
            
            // rotate inertia
            myrot.X = _orientation.X;
            myrot.Y = _orientation.Y;
            myrot.Z = _orientation.Z;
            myrot.W = _orientation.W;

            d.RfromQ(out mymat, ref myrot);
            d.MassRotate(ref objdmass, ref mymat);
                                   
            // set the body rotation and position
            d.BodySetRotation(Body, ref mymat);

            // recompute full object inertia if needed
            if (childrenPrim.Count > 0)
                {

                rcm.X = _position.X + objdmass.c.X;
                rcm.Y = _position.Y + objdmass.c.Y;
                rcm.Z = _position.Z + objdmass.c.Z;

                lock (childrenPrim)
                    {
                    foreach (AuroraODEPrim prm in childrenPrim)
                        {
                        prm.calcdMass(); // recompute inertia on local frame
                        DMassCopy(ref prm.primdMass, ref tmpdmass);

                        // apply prim current rotation to inertia
                        quat.W = prm._orientation.W;
                        quat.X = prm._orientation.X;
                        quat.Y = prm._orientation.Y;
                        quat.Z = prm._orientation.Z;
                        d.RfromQ(out mat, ref quat);                       
                        d.MassRotate(ref tmpdmass, ref mat);

                        Vector3 ppos = prm._position;
                        ppos.X += tmpdmass.c.X - rcm.X;
                        ppos.Y += tmpdmass.c.Y - rcm.Y;
                        ppos.Z += tmpdmass.c.Z - rcm.Z; 

                        // refer inertia to root prim center of mass position
                        d.MassTranslate(ref tmpdmass,
                            ppos.X,
                            ppos.Y,
                            ppos.Z);

                        d.MassAdd(ref objdmass, ref tmpdmass); // add to total object inertia

                        // fix prim colision cats
                        if (prm.prim_geom == IntPtr.Zero)
                            {
                            m_log.Warn("[PHYSICS]: Unable to link one of the linkset elements.  No geom yet");
                            continue;
                            }

                        d.GeomClearOffset(prm.prim_geom);
                        d.GeomSetBody(prm.prim_geom, Body);
                        d.GeomSetOffsetWorldRotation(prm.prim_geom, ref mat); // set relative rotation
                        }
                    }
                }

            d.GeomClearOffset(prim_geom); // make sure we don't have a hidden offset
            // associate root geom with body
            d.GeomSetBody(prim_geom, Body);

            d.BodySetPosition(Body, _position.X + objdmass.c.X, _position.Y + objdmass.c.Y, _position.Z + objdmass.c.Z);
            d.GeomSetOffsetWorldPosition(prim_geom, _position.X, _position.Y, _position.Z);

            d.MassTranslate(ref objdmass, -objdmass.c.X, -objdmass.c.Y, -objdmass.c.Z); // ode wants inertia at center of body
            myrot.W = -myrot.W;
            d.RfromQ(out mymat, ref myrot);
            d.MassRotate(ref objdmass, ref mymat);
            d.BodySetMass(Body, ref objdmass);
            _mass = objdmass.mass;

            m_collisionCategories |= CollisionCategories.Body;
            m_collisionFlags |= (CollisionCategories.Land | CollisionCategories.Wind);

            // disconnect from world gravity so we can apply buoyancy
            if (!testRealGravity)
                d.BodySetGravityMode(Body, false);

            d.BodySetAutoDisableFlag(Body, true);
            d.BodySetAutoDisableSteps(Body, body_autodisable_frames);
//            d.BodySetLinearDampingThreshold(Body, 0.01f);
//            d.BodySetAngularDampingThreshold(Body, 0.001f);
            d.BodySetDamping(Body, .001f, .001f);
            m_disabled = false;

            d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
            d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);

            m_interpenetrationcount = 0;
            m_collisionscore = 0;

            if (m_targetSpace != _parent_scene.space)
                {
                _parent_scene.waitForSpaceUnlock(m_targetSpace);
                if (d.SpaceQuery(m_targetSpace, prim_geom))
                    d.SpaceRemove(m_targetSpace, prim_geom);

                m_targetSpace = _parent_scene.space;
                d.SpaceAdd(m_targetSpace, prim_geom);
                }

            lock (childrenPrim)
                {
                foreach (AuroraODEPrim prm in childrenPrim)
                    {
                    if (prm.prim_geom == IntPtr.Zero)
                        {
                        m_log.Warn("[PHYSICS]: Unable to link one of the linkset elements.  No geom yet");
                        continue;
                        }
                    Vector3 ppos = prm._position;
                    d.GeomSetOffsetWorldPosition(prm.prim_geom, ppos.X, ppos.Y, ppos.Z); // set relative position

                    prm.m_collisionCategories |= CollisionCategories.Body;
                    prm.m_collisionFlags |= (CollisionCategories.Land | CollisionCategories.Wind);
                    d.GeomSetCategoryBits(prm.prim_geom, (int)prm.m_collisionCategories);
                    d.GeomSetCollideBits(prm.prim_geom, (int)prm.m_collisionFlags);

                    prm.Body = Body;
                    prm.m_disabled = false;
                    prm.m_interpenetrationcount = 0;
                    prm.m_collisionscore = 0;
                    _parent_scene.addActivePrim(prm);

                    if (prm.m_targetSpace != _parent_scene.space)
                        {
                        _parent_scene.waitForSpaceUnlock(m_targetSpace);
                        if (d.SpaceQuery(prm.m_targetSpace, prm.prim_geom))
                            d.SpaceRemove(prm.m_targetSpace, prm.prim_geom);

                        prm.m_targetSpace = _parent_scene.space;
                        d.SpaceAdd(m_targetSpace, prm.prim_geom);
                        }
                    }
                }
            // The body doesn't already have a finite rotation mode set here
            if ((!m_angularlock.ApproxEquals(Vector3.One, 0.0f)) && _parent == null)
                {
                createAMotor(m_angularlock);
                }
            if (m_vehicle.Type != Vehicle.TYPE_NONE)
                {
                m_vehicle.Enable(Body, this, _parent_scene);
                }
            _parent_scene.addActivePrim(this);

/*            d.Mass mtmp;
            d.BodyGetMass(Body, out mtmp);
            d.Matrix3 mt = d.GeomGetRotation(prim_geom);
            d.Matrix3 mt2 = d.BodyGetRotation(Body);
            dvtmp = d.GeomGetPosition(prim_geom);
            dbtmp = d.BodyGetPosition(Body);
*/
            }

        public void DestroyBody()  // for now removes all colisions etc from childs, full body reconstruction is needed after this
            {
            //this kills the body so things like 'mesh' can re-create it.
            lock (this)
                {
                if (Body != IntPtr.Zero)
                    _parent_scene.remActivePrim(this);
                m_collisionCategories &= ~CollisionCategories.Body;
                m_collisionFlags &= ~(CollisionCategories.Wind | CollisionCategories.Land);
                if (prim_geom != IntPtr.Zero)
                    {
                    d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
                    d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
                    }
                if (!childPrim)
                    {
                    lock (childrenPrim)
                        {
                        foreach (AuroraODEPrim prm in childrenPrim)
                            {
                            _parent_scene.remActivePrim(prm);
                            prm.m_collisionCategories &= ~CollisionCategories.Body;
                            prm.m_collisionFlags &= ~(CollisionCategories.Wind | CollisionCategories.Land);
                            if (prm.prim_geom != IntPtr.Zero)
                                {
                                d.GeomSetCategoryBits(prm.prim_geom, (int)m_collisionCategories);
                                d.GeomSetCollideBits(prm.prim_geom, (int)m_collisionFlags);
                                }
                            prm.Body = IntPtr.Zero;
                            }
                        }
                    if (Body != IntPtr.Zero)
                        {
                        m_vehicle.Disable(this);
                        d.BodyDestroy(Body);
                        }
                    }             

                _mass = primMass;
                Body = IntPtr.Zero;
                }
            m_disabled = true;
            m_collisionscore = 0;
            }

        #region Mass Calculation

        private float CalculatePrimMass()
        {
            float volume = _size.X * _size.Y * _size.Z; // default
            float tmp;

            float returnMass = 0;
            float hollowAmount = (float)_pbs.ProfileHollow * 2.0e-5f;
            float hollowVolume = hollowAmount * hollowAmount; 
            
            switch (_pbs.ProfileShape)
            {
                case ProfileShape.Square:
                    // default box

                    if (_pbs.PathCurve == (byte)Extrusion.Straight)
                        {
                        if (hollowAmount > 0.0)
                            {
                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Square:
                                case HollowShape.Same:
                                    break;

                                case HollowShape.Circle:

                                    hollowVolume *= 0.78539816339f;
                                    break;

                                case HollowShape.Triangle:

                                    hollowVolume *= (0.5f * .5f);
                                    break;

                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }

                    else if (_pbs.PathCurve == (byte)Extrusion.Curve1)
                        {
                        //a tube 

                        volume *= 0.78539816339e-2f * (float)(200 - _pbs.PathScaleX);
                        tmp= 1.0f -2.0e-2f * (float)(200 - _pbs.PathScaleY);
                        volume -= volume*tmp*tmp;
                        
                        if (hollowAmount > 0.0)
                            {
                            hollowVolume *= hollowAmount;
                            
                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Square:
                                case HollowShape.Same:
                                    break;

                                case HollowShape.Circle:
                                    hollowVolume *= 0.78539816339f;;
                                    break;

                                case HollowShape.Triangle:
                                    hollowVolume *= 0.5f * 0.5f;
                                    break;
                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }

                    break;

                case ProfileShape.Circle:

                    if (_pbs.PathCurve == (byte)Extrusion.Straight)
                        {
                        volume *= 0.78539816339f; // elipse base

                        if (hollowAmount > 0.0)
                            {
                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Same:
                                case HollowShape.Circle:
                                    break;

                                case HollowShape.Square:
                                    hollowVolume *= 0.5f * 2.5984480504799f;
                                    break;

                                case HollowShape.Triangle:
                                    hollowVolume *= .5f * 1.27323954473516f;
                                    break;

                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }

                    else if (_pbs.PathCurve == (byte)Extrusion.Curve1)
                        {
                        volume *= 0.61685027506808491367715568749226e-2f * (float)(200 - _pbs.PathScaleX);
                        tmp = 1.0f - .02f * (float)(200 - _pbs.PathScaleY);
                        volume *= (1.0f - tmp * tmp);
                        
                        if (hollowAmount > 0.0)
                            {

                            // calculate the hollow volume by it's shape compared to the prim shape
                            hollowVolume *= hollowAmount;

                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Same:
                                case HollowShape.Circle:
                                    break;

                                case HollowShape.Square:
                                    hollowVolume *= 0.5f * 2.5984480504799f;
                                    break;

                                case HollowShape.Triangle:
                                    hollowVolume *= .5f * 1.27323954473516f;
                                    break;

                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }
                    break;

                case ProfileShape.HalfCircle:
                    if (_pbs.PathCurve == (byte)Extrusion.Curve1)
                    {
                    volume *= 0.52359877559829887307710723054658f;
                    }
                    break;

                case ProfileShape.EquilateralTriangle:

                    if (_pbs.PathCurve == (byte)Extrusion.Straight)
                        {
                        volume *= 0.32475953f;

                        if (hollowAmount > 0.0)
                            {

                            // calculate the hollow volume by it's shape compared to the prim shape
                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Same:
                                case HollowShape.Triangle:
                                    hollowVolume *= .25f;
                                    break;

                                case HollowShape.Square:
                                    hollowVolume *= 0.499849f * 3.07920140172638f;
                                    break;

                                case HollowShape.Circle:
                                    // Hollow shape is a perfect cyllinder in respect to the cube's scale
                                    // Cyllinder hollow volume calculation

                                    hollowVolume *= 0.1963495f * 3.07920140172638f;
                                    break;

                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }
                    else if (_pbs.PathCurve == (byte)Extrusion.Curve1)
                        {
                        volume *= 0.32475953f;
                        volume *= 0.01f * (float)(200 - _pbs.PathScaleX);
                        tmp = 1.0f - .02f * (float)(200 - _pbs.PathScaleY);
                        volume *= (1.0f - tmp * tmp);

                        if (hollowAmount > 0.0)
                            {

                            hollowVolume *= hollowAmount;

                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Same:
                                case HollowShape.Triangle:
                                    hollowVolume *= .25f;
                                    break;

                                case HollowShape.Square:
                                    hollowVolume *= 0.499849f * 3.07920140172638f;
                                    break;

                                case HollowShape.Circle:

                                    hollowVolume *= 0.1963495f * 3.07920140172638f;
                                    break;

                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }
                        break;                  

                default:
                    break;
                }



            float taperX1;
            float taperY1;
            float taperX;
            float taperY;
            float pathBegin;
            float pathEnd;
            float profileBegin;
            float profileEnd;

            if (_pbs.PathCurve == (byte)Extrusion.Straight || _pbs.PathCurve == (byte)Extrusion.Flexible)
                {
                taperX1 = _pbs.PathScaleX * 0.01f;
                if (taperX1 > 1.0f)
                    taperX1 = 2.0f - taperX1;
                taperX = 1.0f - taperX1;

                taperY1 = _pbs.PathScaleY * 0.01f;
                if (taperY1 > 1.0f)
                    taperY1 = 2.0f - taperY1;
                taperY = 1.0f - taperY1;
                }
            else
                {
                taperX = _pbs.PathTaperX * 0.01f;
                if (taperX < 0.0f)
                    taperX = -taperX;
                taperX1 = 1.0f - taperX;

                taperY = _pbs.PathTaperY * 0.01f;
                if (taperY < 0.0f)
                    taperY = -taperY;               
                taperY1 = 1.0f - taperY;

                }

            volume *= (taperX1 * taperY1 + 0.5f * (taperX1 * taperY + taperX * taperY1) + 0.3333333333f * taperX * taperY);

            pathBegin = (float)_pbs.PathBegin * 2.0e-5f;
            pathEnd = 1.0f - (float)_pbs.PathEnd * 2.0e-5f;
            volume *= (pathEnd - pathBegin);

// this is crude aproximation
            profileBegin = (float)_pbs.ProfileBegin * 2.0e-5f;
            profileEnd = 1.0f - (float)_pbs.ProfileEnd * 2.0e-5f;
            volume *= (profileEnd - profileBegin);

            returnMass = m_density * volume;

            if (returnMass <= 0)
                returnMass = 0.0001f;//ckrinke: Mass must be greater then zero.
//            else if (returnMass > _parent_scene.maximumMassObject)
//                returnMass = _parent_scene.maximumMassObject;

            if (returnMass > _parent_scene.maximumMassObject)
                returnMass = _parent_scene.maximumMassObject;

            primMass = returnMass;
            _mass = primMass;

            return returnMass;
        }// end CalculateMass

        #endregion

        public void calcdMass()
            {



            // very aproximated handling of tortured prims
            Vector3 s;

            s.X = aabb.MaxX - aabb.MinX;
            s.Y = aabb.MaxY - aabb.MinY;
            s.Z = aabb.MaxZ - aabb.MinZ;

            d.MassSetBoxTotal(out primdMass, primMass, s.X, s.Y, s.Z);

            s.X = (aabb.MaxX + aabb.MinX) * 0.5f;
            s.Y = (aabb.MaxY + aabb.MinY) * 0.5f;
            s.Z = (aabb.MaxZ + aabb.MinZ) * 0.5f;

            d.MassTranslate(ref primdMass,
                                s.X,
                                s.Y,
                                s.Z);
            }


        private static Dictionary<IMesh, IntPtr> m_MeshToTriMeshMap = new Dictionary<IMesh, IntPtr>();

        public void setMesh(AuroraODEPhysicsScene parent_scene, IMesh mesh)
        {
            // This sleeper is there to moderate how long it takes between
            // setting up the mesh and pre-processing it when we get rapid fire mesh requests on a single object

            //Thread.Sleep(10);

            //Kill Body so that mesh can re-make the geom
            if (IsPhysical && Body != IntPtr.Zero)
            {
                if (childPrim)
                {
                    if (_parent != null)
                    {
                        AuroraODEPrim parent = (AuroraODEPrim)_parent;
                        parent.ChildDelink(this);
                    }
                }
                else
                {
                    DestroyBody();
                }
            }

            IntPtr vertices, indices;
            int vertexCount, indexCount;
            int vertexStride, triStride;
            mesh.getVertexListAsPtrToFloatArray(out vertices, out vertexStride, out vertexCount); // Note, that vertices are fixed in unmanaged heap
            mesh.getIndexListAsPtrToIntArray(out indices, out triStride, out indexCount); // Also fixed, needs release after usage

            mesh.releaseSourceMeshData(); // free up the original mesh data to save memory
            if (m_MeshToTriMeshMap.ContainsKey(mesh))
            {
                _triMeshData = m_MeshToTriMeshMap[mesh];
            }
            else
            {
                _triMeshData = d.GeomTriMeshDataCreate();

                d.GeomTriMeshDataBuildSimple(_triMeshData, vertices, vertexStride, vertexCount, indices, indexCount, triStride);
                d.GeomTriMeshDataPreprocess(_triMeshData);
                m_MeshToTriMeshMap[mesh] = _triMeshData;
            }

            _parent_scene.waitForSpaceUnlock(m_targetSpace);
            try
            {
                if (prim_geom == IntPtr.Zero)
                {
                    SetGeom(d.CreateTriMesh(m_targetSpace, _triMeshData, parent_scene.triCallback, null, null));
                }
            }
            catch (AccessViolationException)
            {
                m_log.Error("[PHYSICS]: MESH LOCKED");
                return;
            }
        }

        private void changeAngularLock(Object arg)
            {
            Vector3 newlock = (Vector3)arg;
            // do we have a Physical object?
            if (Body != IntPtr.Zero)
                {
                //Check that we have a Parent
                //If we have a parent then we're not authorative here
                if (_parent == null)
                    {

                    if (!newlock.ApproxEquals(Vector3.One, 0f))
                        {
                        createAMotor(newlock);
                        }
                    else
                        {
                        if (Amotor != IntPtr.Zero)
                            {
                            d.JointDestroy(Amotor);
                            Amotor = IntPtr.Zero;
                            }
                        }
                    }
                }
            // Store this for later in case we get turned into a separate body
            m_angularlock = newlock;
            }

        private void changelink(AuroraODEPrim newparent)
            {
            // If the newly set parent is not null
            // create link
            if (_parent == null && newparent != null)
                {
                if (newparent.PhysicsActorType == (int)ActorTypes.Prim)
                    {
                    AuroraODEPrim obj = (AuroraODEPrim)newparent;
                    obj.ParentPrim(this);
                    }
                }
            // If the newly set parent is null
            // destroy link
            else if (_parent != null && newparent == null)
                {
                //Console.WriteLine("  changelink B");

                if (_parent is AuroraODEPrim)
                    {
                    AuroraODEPrim obj = (AuroraODEPrim)_parent;
                    obj.ChildDelink(this);
                    childPrim = false;
                    //_parent = null;
                    }
                }

            _parent = newparent;
            }

        // I'm the parent
        // prim is the child
        public void ParentPrim(AuroraODEPrim prim)
            {
            //Console.WriteLine("ParentPrim  " + m_primName);
            if (this.m_localID != prim.m_localID)
                {
                lock (childrenPrim)
                    {
                    if (!childrenPrim.Contains(prim)) // must allow full reconstruction
                        childrenPrim.Add(prim);
                    }
                prim.childPrim = true;
                prim._parent = this;

                if (prim.Body != IntPtr.Zero && prim.Body != Body)
                    {
                    prim.DestroyBody(); // don't loose bodies around
                    prim.Body = IntPtr.Zero;
                    }
                if (m_isphysical)
                    MakeBody(); // full nasty reconstruction
                }
            }       

        private void ChildSetGeom(AuroraODEPrim odePrim)
        {           
            DestroyBody();
            MakeBody();
        }


        private void UpdateChildsfromgeom()
            {
            if(childrenPrim.Count >0)
                {
                foreach (AuroraODEPrim prm in childrenPrim)
                    prm.UpdateDataFromGeom();
                }
            }

        private void UpdateDataFromGeom()
            {
            if (prim_geom != IntPtr.Zero)
                {
                d.Vector3 lpos = d.GeomGetPosition(prim_geom);
                _position.X = lpos.X;
                _position.Y = lpos.Y;
                _position.Z = lpos.Z;
                d.Quaternion qtmp = new d.Quaternion { };
                d.GeomCopyQuaternion(prim_geom, out qtmp);
                _orientation.W = qtmp.W;
                _orientation.X = qtmp.X;
                _orientation.Y = qtmp.Y;
                _orientation.Z = qtmp.Z;
                }
            }

        private void ChildDelink(AuroraODEPrim odePrim)
            {
            // Okay, we have a delinked child.. destroy all body and remake
            if (odePrim != this && !childrenPrim.Contains(odePrim))
                return;

            DestroyBody();

            if (odePrim == this)
                {
                AuroraODEPrim newroot = null;
                lock (childrenPrim)
                    {
                    if (childrenPrim.Count > 0)
                        {
                        newroot = childrenPrim[0];
                        childrenPrim.RemoveAt(0);
                        foreach (AuroraODEPrim prm in childrenPrim)
                            {
                            newroot.childrenPrim.Add(prm);
                            }
                        childrenPrim.Clear();
                        }
                    if (newroot != null)
                        {
                        newroot.childPrim = false;
                        newroot._parent = null;
                        newroot.MakeBody();
                        }
                    }
                }

            else
                {
                lock (childrenPrim)
                    {
                    childrenPrim.Remove(odePrim);
                    odePrim.childPrim = false;
                    odePrim._parent = null;
                    odePrim.UpdateDataFromGeom();
                    odePrim.MakeBody();
                    }
                }

            MakeBody();
            }

        private void ChildRemove(AuroraODEPrim odePrim)
            {
            // Okay, we have a delinked child.. destroy all body and remake
            if (odePrim != this && !childrenPrim.Contains(odePrim))
                return;

            DestroyBody();

            if (odePrim == this)
                {
                AuroraODEPrim newroot = null;
                lock (childrenPrim)
                    {
                    if (childrenPrim.Count > 0)
                        {
                        newroot = childrenPrim[0];
                        childrenPrim.RemoveAt(0);
                        foreach (AuroraODEPrim prm in childrenPrim)
                            {
                            newroot.childrenPrim.Add(prm);
                            }
                        childrenPrim.Clear();
                        }
                    if (newroot != null)
                        {
                        newroot.childPrim = false;
                        newroot._parent = null;
                        newroot.MakeBody();
                        }
                    }               
                return;
                }
            else
                {
                lock (childrenPrim)
                    {
                    childrenPrim.Remove(odePrim);
                    odePrim.childPrim = false;
                    odePrim._parent = null;
                    }
                }

            MakeBody();
            }

        private void changeSelectedStatus(bool newsel)
        {
            bool isphys = IsPhysical;

            if (newsel)
            {

                m_collisionCategories = CollisionCategories.Selected;
                m_collisionFlags = (CollisionCategories.Sensor | CollisionCategories.Space);

                // We do the body disable soft twice because 'in theory' a collision could have happened
                // in between the disabling and the collision properties setting
                // which would wake the physical body up from a soft disabling and potentially cause it to fall
                // through the ground.
                
                // NOTE FOR JOINTS: this doesn't always work for jointed assemblies because if you select
                // just one part of the assembly, the rest of the assembly is non-selected and still simulating,
                // so that causes the selected part to wake up and continue moving.

                // even if you select all parts of a jointed assembly, it is not guaranteed that the entire
                // assembly will stop simulating during the selection, because of the lack of atomicity
                // of select operations (their processing could be interrupted by a thread switch, causing
                // simulation to continue before all of the selected object notifications trickle down to
                // the physics engine).

                // e.g. we select 100 prims that are connected by joints. non-atomically, the first 50 are
                // selected and disabled. then, due to a thread switch, the selection processing is
                // interrupted and the physics engine continues to simulate, so the last 50 items, whose
                // selection was not yet processed, continues to simulate. this wakes up ALL of the 
                // first 50 again. then the last 50 are disabled. then the first 50, which were just woken
                // up, start simulating again, which in turn wakes up the last 50.

                if (isphys)
                {
                    disableBodySoft();
                }

                if (prim_geom != IntPtr.Zero)
                {
                    d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
                    d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
                }

                if (isphys)
                {
                    disableBodySoft();
                }
            }
            else
            {
                m_collisionCategories = CollisionCategories.Geom;

                if (isphys)
                    m_collisionCategories |= CollisionCategories.Body;

                m_collisionFlags = m_default_collisionFlags;

                if (m_collidesLand)
                    m_collisionFlags |= CollisionCategories.Land;
                if (m_collidesWater)
                    m_collisionFlags |= CollisionCategories.Water;

                if (prim_geom != IntPtr.Zero)
                {
                    d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
                    d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
                }
                if (isphys)
                {
                    if (Body != IntPtr.Zero)
                    {
                        d.BodySetLinearVel(Body, 0f, 0f, 0f);
                        d.BodySetAngularVel(Body, 0f, 0f, 0f);
                        d.BodySetForce(Body, 0, 0, 0);
                        d.BodySetTorque(Body, 0, 0, 0);
                        enableBodySoft();
                    }
                }
            }

            resetCollisionAccounting();
            m_isSelected = newsel;
        }//end changeSelectedStatus



        public void CreateGeom(IntPtr m_targetSpace, IMesh _mesh)
            {
            //Console.WriteLine("CreateGeom:");
            if (_mesh != null)
                {
                setMesh(_parent_scene, _mesh); // this will give a mesh to non trivial known prims
                }
            else
                {
                if (_pbs.ProfileShape == ProfileShape.HalfCircle && _pbs.PathCurve == (byte)Extrusion.Curve1
                    && _size.X == _size.Y && _size.Y == _size.Z)
                    { // it's a sphere
                    _parent_scene.waitForSpaceUnlock(m_targetSpace);
                    try
                        {                       
                        SetGeom(d.CreateSphere(m_targetSpace, _size.X / 2));
                        }
                    catch (AccessViolationException)
                        {
                        m_log.Warn("[PHYSICS]: Unable to create physics proxy for object");
                        ode.dunlock(_parent_scene.world);
                        return;
                        }
                    }
                else
                    {
                    _parent_scene.waitForSpaceUnlock(m_targetSpace);
                    try
                        {
                        //Console.WriteLine("  CreateGeom 4");
                        SetGeom(d.CreateBox(m_targetSpace, _size.X, _size.Y, _size.Z));
                        }
                    catch (AccessViolationException)
                        {
                        m_log.Warn("[PHYSICS]: Unable to create physics proxy for object");
                        ode.dunlock(_parent_scene.world);
                        return;
                        }
                    }
                }
            }
        public void changeadd()
            {
//            int[] iprimspaceArrItem = _parent_scene.calculateSpaceArrayItemFromPos(_position);
            IntPtr targetspace = _parent_scene.calculateSpaceForGeom(_position);

//            if (targetspace == IntPtr.Zero)
//                targetspace = _parent_scene.createprimspace(iprimspaceArrItem[0], iprimspaceArrItem[1]);

            m_targetSpace = targetspace;

            if (_mesh == null)
                {
                if (_parent_scene.needsMeshing(_pbs))
                    {
                    // Don't need to re-enable body..   it's done in SetMesh
                    _mesh = _parent_scene.mesher.CreateMesh(m_primName, _pbs, _size, _parent_scene.meshSculptLOD, IsPhysical);
                    // createmesh returns null when it's a shape that isn't a cube.
                    // m_log.Debug(m_localID);
                    }
                }


            lock (_parent_scene.OdeLock)
                {
                //Console.WriteLine("changeadd 1");
                CreateGeom(m_targetSpace, _mesh);

                if (prim_geom != IntPtr.Zero)
                    {

                    CalculatePrimMass();
/*
                    if (m_isphysical)
                        MakeBody();

                    else
*/
                        {                       
                        d.GeomSetPosition(prim_geom, _position.X, _position.Y, _position.Z);
                        d.Quaternion myrot = new d.Quaternion();
                        myrot.X = _orientation.X;
                        myrot.Y = _orientation.Y;
                        myrot.Z = _orientation.Z;
                        myrot.W = _orientation.W;
                        d.GeomSetQuaternion(prim_geom, ref myrot);
                        }
                    _parent_scene.geom_name_map[prim_geom] = this.m_primName;
                    _parent_scene.actor_name_map[prim_geom] = (PhysicsActor)this;
                    }
                }
            changeSelectedStatus(m_isSelected);
            }

        public void changemoveandrotate(Vector3 newpos,Quaternion newrot)
            {
            if (IsPhysical)
                {
                if (childPrim)  // inertia is messed, must rebuild
                    {
                    AuroraODEPrim parent = (AuroraODEPrim)_parent;
                    parent.DestroyBody();
                    parent.MakeBody();
                    }
                else
                    {
                    if (newrot != _orientation)
                        {
                        d.Quaternion myrot = new d.Quaternion();
                        myrot.X = newrot.X;
                        myrot.Y = newrot.Y;
                        myrot.Z = newrot.Z;
                        myrot.W = newrot.W;
                        d.GeomSetQuaternion(prim_geom, ref myrot);
                        if (Body != IntPtr.Zero && !m_angularlock.ApproxEquals(Vector3.One, 0f))
                            createAMotor(m_angularlock);
                        }
                    d.GeomSetPosition(prim_geom, newpos.X, newpos.Y, newpos.Z);
                    }
                if (Body != IntPtr.Zero)
                    d.BodyEnable(Body);
                }

            else
                {
                // string primScenAvatarIn = _parent_scene.whichspaceamIin(_position);
                // int[] arrayitem = _parent_scene.calculateSpaceArrayItemFromPos(_position);
                _parent_scene.waitForSpaceUnlock(m_targetSpace);

                IntPtr tempspace = _parent_scene.recalculateSpaceForGeom(prim_geom, newpos, m_targetSpace);
                m_targetSpace = tempspace;

                _parent_scene.waitForSpaceUnlock(m_targetSpace);

                if (newrot != _orientation)
                    {
                    d.Quaternion myrot = new d.Quaternion();
                    myrot.X = newrot.X;
                    myrot.Y = newrot.Y;
                    myrot.Z = newrot.Z;
                    myrot.W = newrot.W;
                    d.GeomSetQuaternion(prim_geom, ref myrot);
                    }
                d.GeomSetPosition(prim_geom, newpos.X, newpos.Y, newpos.Z);

                _parent_scene.waitForSpaceUnlock(m_targetSpace);
                d.SpaceAdd(m_targetSpace, prim_geom);
                }

            _orientation=newrot;
            _position = newpos;
            fakepos = false;
            fakeori = false;
            changeSelectedStatus(m_isSelected);

            resetCollisionAccounting();
            }

        public void Move(float timestep)
            {
            if (m_frozen)
                return;
            float fx = 0;
            float fy = 0;
            float fz = 0;


            if (m_isphysical && (Body != IntPtr.Zero) && !m_isSelected && !childPrim)        // KF: Only move root prims.
                {
                if (m_vehicle.Type != Vehicle.TYPE_NONE)
                    {
                    // 'VEHICLES' are dealt with in ODEDynamics.cs
                    m_vehicle.Step(Body, timestep, _parent_scene, this);
                    d.Vector3 vel = d.BodyGetLinearVel(Body);
                    _velocity = new Vector3((float)vel.X, (float)vel.Y, (float)vel.Z);
                    d.Vector3 pos = d.GeomGetPosition(prim_geom);
                    _position = new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z);
                    _zeroFlag = false;
                    }
                else
                    {
                    float m_mass = _mass;
                    d.Vector3 dcpos = d.BodyGetPosition(Body);
                    d.Vector3 vel = d.BodyGetLinearVel(Body);
                    d.Vector3 angvel = d.BodyGetAngularVel(Body);

                    //KF: m_buoyancy should be set by llSetBuoyancy() for non-vehicle.
                    // would come from SceneObjectPart.cs, public void SetBuoyancy(float fvalue) , PhysActor.Buoyancy = fvalue; ??
                    // m_buoyancy: (unlimited value) <0=Falls fast; 0=1g; 1=0g; >1 = floats up 
                    // gravityz multiplier = 1 - m_buoyancy
                    if (!_parent_scene.UsePointGravity)
                        {
                        if (!testRealGravity)
                            {
                            fx = _parent_scene.gravityx * (1.0f - m_buoyancy);
                            fy = _parent_scene.gravityy * (1.0f - m_buoyancy);
                            fz = _parent_scene.gravityz * (1.0f - m_buoyancy);
                            }
                        else
                            {
                            fx = _parent_scene.gravityx * -1 * (1.0f - m_buoyancy);
                            fy = _parent_scene.gravityy * -1 * (1.0f - m_buoyancy);
                            fz = _parent_scene.gravityz * -1 * (1.0f - m_buoyancy);
                            }
                        }
                    else
                        {
                        //Set up point gravity for this object
                        Vector3 cog = _parent_scene.PointOfGravity;
                        if (cog.X != 0)
                            fx = (cog.X - dcpos.X);
                        if (cog.Y != 0)
                            fy = (cog.Y - dcpos.Y);
                        if (cog.Z != 0)
                            fz = (cog.Z - dcpos.Z);
                        }


                    #region PID
                    if (m_usePID)
                        {
                        //Console.WriteLine("PID " +  m_primName);
                        // KF - this is for object move? eg. llSetPos() ?
                        //if (!d.BodyIsEnabled(Body))
                        //d.BodySetForce(Body, 0f, 0f, 0f);
                        // If we're using the PID controller, then we have no gravity
                        //fz = (-1 * _parent_scene.gravityz) * m_mass;     //KF: ?? Prims have no global gravity,so simply...
                        fz = 0f;

                        //  no lock; for now it's only called from within Simulate()

                        // If the PID Controller isn't active then we set our force
                        // calculating base velocity to the current position

                        if ((m_PIDTau < 1) && (m_PIDTau != 0))
                            {
                            //PID_G = PID_G / m_PIDTau;
                            m_PIDTau = 1;
                            }

                        if ((PID_G - m_PIDTau) <= 0)
                            {
                            PID_G = m_PIDTau + 1;
                            }
                        //PidStatus = true;

                        // PhysicsVector vec = new PhysicsVector();

                        _target_velocity =
                            new Vector3(
                                (float)(m_PIDTarget.X - dcpos.X) * ((/*PID_G - */m_PIDTau) * timestep),
                                (float)(m_PIDTarget.Y - dcpos.Y) * ((/*PID_G - */m_PIDTau) * timestep),
                                (float)(m_PIDTarget.Z - dcpos.Z) * ((/*PID_G - */m_PIDTau) * timestep)
                                );

                        //  if velocity is zero, use position control; otherwise, velocity control

                        if (_target_velocity.ApproxEquals(Vector3.Zero, 0.05f))
                            {
                            //  keep track of where we stopped.  No more slippin' & slidin'

                            // We only want to deactivate the PID Controller if we think we want to have our surrogate
                            // react to the physics scene by moving it's position.
                            // Avatar to Avatar collisions
                            // Prim to avatar collisions

                            //fx = (_target_velocity.X - vel.X) * (PID_D) + (_zeroPosition.X - pos.X) * (PID_P * 2);
                            //fy = (_target_velocity.Y - vel.Y) * (PID_D) + (_zeroPosition.Y - pos.Y) * (PID_P * 2);
                            //fz = fz + (_target_velocity.Z - vel.Z) * (PID_D) + (_zeroPosition.Z - pos.Z) * PID_P;
                            d.BodySetPosition(Body, m_PIDTarget.X, m_PIDTarget.Y, m_PIDTarget.Z);
                            if (!m_angularlock.ApproxEquals(Vector3.One, 0.003f) &&
                                Amotor != IntPtr.Zero)
                                {

                                }
                            d.BodySetLinearVel(Body, 0, 0, 0);
                            d.BodyAddForce(Body, 0, 0, fz);

                            return;
                            }
                        else
                            {
                            _zeroFlag = false;

                            // We're flying and colliding with something
                            fx = (float)((_target_velocity.X) - vel.X) * (PID_D);
                            fy = (float)((_target_velocity.Y) - vel.Y) * (PID_D);

                            // vec.Z = (_target_velocity.Z - vel.Z) * PID_D + (_zeroPosition.Z - pos.Z) * PID_P;

                            fz = (float)(fz + ((_target_velocity.Z - vel.Z) * (PID_D)));
                            }
                        }        // end if (m_usePID)
                    #endregion
                    #region Hover PID
                    // Hover PID Controller needs to be mutually exlusive to MoveTo PID controller
                    if (m_useHoverPID && !m_usePID)
                        {
                        //Console.WriteLine("Hover " +  m_primName);

                        // If we're using the PID controller, then we have no gravity
                        fx = -_parent_scene.gravityx;
                        fy = -_parent_scene.gravityy;
                        fz = -_parent_scene.gravityz;

                        //  no lock; for now it's only called from within Simulate()

                        // If the PID Controller isn't active then we set our force
                        // calculating base velocity to the current position

                        if ((m_PIDTau < 1))
                            {
                            PID_G = PID_G / m_PIDTau;
                            }

                        if ((PID_G - m_PIDTau) <= 0)
                            {
                            PID_G = m_PIDTau + 1;
                            }


                        // Where are we, and where are we headed?

                        //    Non-Vehicles have a limited set of Hover options.
                        // determine what our target height really is based on HoverType
                        switch (m_PIDHoverType)
                            {
                            case PIDHoverType.Ground:
                                m_groundHeight = _parent_scene.GetTerrainHeightAtXY((float)dcpos.X, (float)dcpos.Y);
                                m_targetHoverHeight = m_groundHeight + m_PIDHoverHeight;
                                break;
                            case PIDHoverType.GroundAndWater:
                                m_groundHeight = _parent_scene.GetTerrainHeightAtXY((float)dcpos.X, (float)dcpos.Y);
                                m_waterHeight = (float)_parent_scene.GetWaterLevel((float)dcpos.X, (float)dcpos.Y);
                                if (m_groundHeight > m_waterHeight)
                                    {
                                    m_targetHoverHeight = m_groundHeight + m_PIDHoverHeight;
                                    }
                                else
                                    {
                                    m_targetHoverHeight = m_waterHeight + m_PIDHoverHeight;
                                    }
                                break;

                            }     // end switch (m_PIDHoverType)


                        _target_velocity =
                            new Vector3(0.0f, 0.0f,
                                (float)(m_targetHoverHeight - dcpos.Z) * ((PID_G - m_PIDHoverTau) * timestep)
                                );

                        //  if velocity is zero, use position control; otherwise, velocity control

                        if (_target_velocity.ApproxEquals(Vector3.Zero, 0.1f))
                            {
                            //  keep track of where we stopped.  No more slippin' & slidin'

                            // We only want to deactivate the PID Controller if we think we want to have our surrogate
                            // react to the physics scene by moving it's position.
                            // Avatar to Avatar collisions
                            // Prim to avatar collisions

                            d.BodySetPosition(prim_geom, dcpos.X, dcpos.Y, m_targetHoverHeight);
                            d.BodySetLinearVel(Body, vel.X, vel.Y, 0);
                            d.BodyAddForce(Body, 0, 0, fz);
                            return;
                            }
                        else
                            {
                            _zeroFlag = false;

                            // We're flying and colliding with something
                            fz = (float)(fz + ((_target_velocity.Z - vel.Z) * (PID_D)));
                            }
                        }
                    #endregion

                    fx *= m_mass;
                    fy *= m_mass;
                    fz *= m_mass;

                    fx += m_force.X;
                    fy += m_force.Y;
                    fz += m_force.Z;

                    # region drag and forces accumulators

                    float drag = -m_mass * 0.2f;

                    fx += drag * vel.X;
                    fy += drag * vel.Y;
                    fz += drag * vel.Z;

                   
                    Vector3 newtorque;
                    newtorque.X = m_angularforceacc.X;
                    newtorque.Y = m_angularforceacc.Y;
                    newtorque.Z = m_angularforceacc.Z;
                    m_angularforceacc = Vector3.Zero;

                    fx += m_forceacc.X;
                    fy += m_forceacc.Y;
                    fz += m_forceacc.Z;
                    m_forceacc = Vector3.Zero;

                    #endregion

                    //m_log.Info("[OBJPID]: X:" + fx.ToString() + " Y:" + fy.ToString() + " Z:" + fz.ToString());
                    if (fx != 0 || fy != 0 || fz != 0 || newtorque.X != 0 || newtorque.Y != 0 || newtorque.Z != 0)
                        {
                        // 35n times the mass per second applied maximum.
                        float nmax = 35f * m_mass;
                        float nmin = -35f * m_mass;

                        if (fx > nmax)
                            fx = nmax;
                        if (fx < nmin)
                            fx = nmin;
                        if (fy > nmax)
                            fy = nmax;
                        if (fy < nmin)
                            fy = nmin;

                        if (Amotor != IntPtr.Zero && !m_angularlock.ApproxEquals(Vector3.One, 0.003f)                                )
                            {
                            d.JointSetAMotorParam(Amotor, (int)dParam.LowStop, -0.001f);
                            d.JointSetAMotorParam(Amotor, (int)dParam.LoStop3, -0.0001f);
                            d.JointSetAMotorParam(Amotor, (int)dParam.LoStop2, -0.0001f);
                            d.JointSetAMotorParam(Amotor, (int)dParam.HiStop, 0.0001f);
                            d.JointSetAMotorParam(Amotor, (int)dParam.HiStop3, 0.0001f);
                            d.JointSetAMotorParam(Amotor, (int)dParam.HiStop2, 0.0001f);
                            }
/*
                        if (vel.Z < -30)
                            {
                            vel.Z = -30;
                            }
*/
                        bool disabled = false;
/*
                        if (_parent_scene.m_DisableSlowPrims)
                            {
                            if (((float)fz == (float)(_parent_scene.gravityz * m_mass)) &&
                                (Math.Abs(vel.X) < 0.01 || Math.Abs(vel.Y) < 0.01 || Math.Abs(vel.Z) < 0.0001))
                                {
                                if (Math.Abs(vel.X) < 0.0001 || Math.Abs(vel.Y) < 0.0001 || Math.Abs(vel.Z) < 0.0001)
                                    {
                                    Vector3 angvelocity = new Vector3((float)angvel.X, (float)angvel.Y, (float)angvel.Z);

                                    if (angvelocity.ApproxEquals(Vector3.Zero, 0.005f) &&
                                        vel.X != 0 && vel.Y != 0 && vel.Z != 0)
                                        {
                                        if (d.BodyIsEnabled(Body))
                                            {
                                            d.BodySetLinearVel(Body, 0, 0, 0);
                                            d.BodySetForce(Body, 0, 0, 0);
                                            d.BodyDisable(Body);
                                            disabled = true;
                                            }
                                        }
                                    else
                                        {
                                        if (!d.BodyIsEnabled(Body))
                                            d.BodyEnable(Body);
                                        }
                                    }
                                else
                                    {
                                    if (!d.BodyIsEnabled(Body))
                                        {
                                        d.BodyEnable(Body);
                                        fz = 100 * m_mass;
                                        }
                                    }
                                }
                            }
*/
                        if (!disabled)
                            {
                            if (!d.BodyIsEnabled(Body))
                                {
                                enableBodySoft();                               
                                }

                            d.BodyAddForce(Body, fx, fy, fz);
                            d.BodyAddTorque(Body, newtorque.X, newtorque.Y, newtorque.Z);                    

                            }
                        }
                    }
                }
            else
                {    // is not physical, or is not a body or is selected
                //  _zeroPosition = d.BodyGetPosition(Body);
                return;
                //Console.WriteLine("Nothing " +  m_primName);

                }           
            }

        private d.Quaternion ConvertTodQuat(Quaternion q)
        {
            d.Quaternion dq = new d.Quaternion();
            dq.X = q.X;
            dq.Y = q.Y;
            dq.Z = q.Z;
            dq.W = q.W;
            return dq;
        }

        private void resetCollisionAccounting()
        {
            m_collisionscore = 0;
            m_interpenetrationcount = 0;
            m_disabled = false;
        }

        public void changedisable()
        {
            m_disabled = true;
            if (Body != IntPtr.Zero)
            {
                d.BodyDisable(Body);
                Body = IntPtr.Zero;
            }

        }

        public void changePhysicsStatus(bool newphys)
            {
            m_isphysical = newphys;
            if (!childPrim)
                {
                if (newphys == true)
                    {
                    if (Body == IntPtr.Zero)
                        {
                        if (_pbs.SculptEntry && _parent_scene.meshSculptedPrim)
                            {
                            changeshape((object) _pbs);
                            }
                        else
                            {
                            MakeBody();
                            }
                        }
                    }
                else
                    {
                    if (Body != IntPtr.Zero)
                        {
                        UpdateChildsfromgeom();
                        if (_pbs.SculptEntry && _parent_scene.meshSculptedPrim)
                            {
                            changeshape((object)_pbs);
                            }
                        else
                            DestroyBody();
                        }
                    }
                }
            
            changeSelectedStatus(m_isSelected);
            resetCollisionAccounting();
            }

       

        public void changefloatonwater(object arg)
        {
        m_collidesWater = (bool) arg;

            if (prim_geom != IntPtr.Zero)
            {
                if (m_collidesWater)
                {
                    m_collisionFlags |= CollisionCategories.Water;
                }
                else
                {
                    m_collisionFlags &= ~CollisionCategories.Water;
                }
                d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
            }
        }


        public void changeprimsizeshape()
            {

            _parent_scene.geom_name_map.Remove(prim_geom);
            _parent_scene.actor_name_map.Remove(prim_geom);

            bool chp = childPrim;
            // Cleanup of old prim geometry and Bodies
            if (IsPhysical && Body != IntPtr.Zero)
                {
                if (chp)
                    {
                    if (_parent != null)
                        {
                        AuroraODEPrim parent = (AuroraODEPrim)_parent;
                        parent.ChildDelink(this);
                        }
                    }
                else
                    {
                    DestroyBody();
                    }
                }
            if (prim_geom != IntPtr.Zero)
                {
                try
                    {
                    d.GeomDestroy(prim_geom);
                    }
                catch (System.AccessViolationException)
                    {
                    prim_geom = IntPtr.Zero;
                    m_log.Error("[PHYSICS]: PrimGeom dead");
                    }
                prim_geom = IntPtr.Zero;
                }
            // we don't need to do space calculation because the client sends a position update also.
            if (_size.X <= 0) _size.X = 0.01f;
            if (_size.Y <= 0) _size.Y = 0.01f;
            if (_size.Z <= 0) _size.Z = 0.01f;
            // Construction of new prim

            if (_parent_scene.needsMeshing(_pbs))
                {
                // Don't need to re-enable body..   it's done in SetMesh
                float meshlod = _parent_scene.meshSculptLOD;

                if (IsPhysical)
                    meshlod = _parent_scene.MeshSculptphysicalLOD;

                IMesh mesh = _parent_scene.mesher.CreateMesh(m_primName, _pbs, _size, meshlod, IsPhysical);
                // createmesh returns null when it doesn't mesh.
                CreateGeom(m_targetSpace, mesh);
                }
            else
                {
                _mesh = null;
                //Console.WriteLine("changeshape");
                CreateGeom(m_targetSpace, null);
                }

            lock (_parent_scene.OdeLock)
                {
                if (prim_geom != IntPtr.Zero)
                    {
                    CalculatePrimMass();

                    if (m_isphysical && !chp)
                        MakeBody();
                    else
                        {
                        d.GeomSetPosition(prim_geom, _position.X, _position.Y, _position.Z);
                        d.Quaternion myrot = new d.Quaternion();
                        myrot.X = _orientation.X;
                        myrot.Y = _orientation.Y;
                        myrot.Z = _orientation.Z;
                        myrot.W = _orientation.W;
                        d.GeomSetQuaternion(prim_geom, ref myrot);
                        }

                    _parent_scene.geom_name_map[prim_geom] = this.m_primName;
                    _parent_scene.actor_name_map[prim_geom] = (PhysicsActor)this;
                    }
                }

            changeSelectedStatus(m_isSelected);

            if (chp)
                {
                if (_parent is AuroraODEPrim)
                    {
                    AuroraODEPrim parent = (AuroraODEPrim)_parent;
                    parent.ChildSetGeom(this);
                    }
                }
            resetCollisionAccounting();
            }

        public void changeshape(object arg)
            {
            _pbs = (PrimitiveBaseShape) arg;
            changeprimsizeshape();
            }

        public void changesize(object arg)
            {
            _size = (Vector3)arg;
            changeprimsizeshape();
            }

        public void changeAddForce(object arg)
            {
            if (!m_isSelected)
                {
                if (IsPhysical)
                    {
                    if (m_vehicle.Type == Vehicle.TYPE_NONE)
                        {
                        m_forceacc += (Vector3)arg *100;
                        }
                    else
                        {
                        m_vehicle.ProcessForceTaint((Vector3)arg);
                        }
                    }
                m_collisionscore = 0;
                m_interpenetrationcount = 0;
                }
            }
        public void changeSetTorque(Object arg)
        {
            Vector3 newtorque = (Vector3) arg;
            if (!m_isSelected)
            {
                if (IsPhysical && Body != IntPtr.Zero)
                {
                d.BodySetTorque(Body, newtorque.X, newtorque.Y, newtorque.Z);
                }
            }
        }

        public void changeAddAngularForce(object arg)
            {
            if (!m_isSelected)
                {
                //m_log.Info("[PHYSICS]: dequeing forcelist");
                if (IsPhysical)
                    {
                    m_angularforceacc += (Vector3)arg * 100;
                    }

                m_collisionscore = 0;
                m_interpenetrationcount = 0;
                }
            }

        private void changevelocity(object arg)
            {
            _velocity = (Vector3)arg;
            if (!m_isSelected)
                {
                Thread.Sleep(20);
                if (IsPhysical)
                    {
                    if (Body != IntPtr.Zero)
                        {
                        d.BodySetLinearVel(Body, _velocity.X, _velocity.Y, _velocity.Z);
                        }
                    }

                //resetCollisionAccounting();
                }
            }

        public override bool IsPhysical
        {
            get {
                if (childPrim && _parent != null)  // root prim defines if is physical or not
                    return ((AuroraODEPrim)_parent).m_isphysical;
                else
                    return m_isphysical;
                }
            set {
                AddChange(changes.Physical, (object)value);
                if (!(bool)value) // Zero the remembered last velocity
                      m_lastVelocity = Vector3.Zero;
                }
        }

        public void setPrimForRemoval()
        {
            AddChange(changes.Remove, null);
        }

        public override bool Flying
        {
            // no flying prims for you
            get { return false; }
            set { }
        }

        public override bool IsColliding
        {
            get { return iscolliding; }
            set { iscolliding = value; }
        }

        public override bool CollidingGround
        {
            get { return false; }
            set { return; }
        }

        public override bool CollidingObj
        {
            get { return false; }
            set { return; }
        }

        public override bool ThrottleUpdates
        {
            get { return m_throttleUpdates; }
            set { m_throttleUpdates = value; }
        }

        public override bool Stopped
        {
            get { return _zeroFlag; }
        }

        public override Vector3 Position
            {
            get
                {
                if (fakepos)
                    return showposition;
                else
                    return _position;
                }

            set
                {
                showposition = value;
                fakepos = true;
                AddChange(changes.Position, (object)value);
                //m_log.Info("[PHYSICS]: " + _position.ToString());
                }
            }

        public override Vector3 Size
        {
            get { return _size; }
            set
            {
                if (value.IsFinite())
                {
                    AddChange(changes.Size,(object) value);
                }
                else
                {
                    m_log.Warn("[PHYSICS]: Got NaN Size on object");
                }
            }
        }
       

        public override float Mass
        {
            get
            { 
                return _mass;
            }
            set
            {
                _mass = value; // ??????
            }
        }

        public override Vector3 Force
        {
            //get { return Vector3.Zero; }
            get { return m_force; }
            set
            {
                if (value.IsFinite())
                {
                    AddChange(changes.Force,(object) value);
                }
                else
                {
                    m_log.Warn("[PHYSICS]: NaN in Force Applied to an Object");
                }
            }
        }

        public override int VehicleType
        {
            get { return (int)m_vehicle.Type; }
            set
            {
                if (m_vehicle.Type != Vehicle.TYPE_NONE)
                {
                    m_vehicle.Enable(Body, this, _parent_scene);
                }
                m_vehicle.ProcessTypeChange((Vehicle)value); 
            }
        }

        public override void VehicleFloatParam(int param, float value)
        {
            m_vehicle.ProcessFloatVehicleParam((Vehicle) param, value);
        }

        public override void VehicleVectorParam(int param, Vector3 value)
        {
            m_vehicle.ProcessVectorVehicleParam((Vehicle) param, value);
        }

        public override void VehicleRotationParam(int param, Quaternion rotation)
        {
            m_vehicle.ProcessRotationVehicleParam((Vehicle) param, rotation);
        }

        public override void VehicleFlags(int param, bool remove)
        {
            m_vehicle.ProcessVehicleFlags(param, remove);
        }

        public override void SetCameraPos(Vector3 CameraRotation)
        {
            m_vehicle.ProcessSetCameraPos(CameraRotation);
        }

        public override void SetVolumeDetect(int param)
        {
            lock (_parent_scene.OdeLock)
            {
            AddChange(changes.VolumeDtc, (object)param);
            }
        }

        public override Vector3 CenterOfMass
        {
            get {
                
                d.Vector3 dtmp;

                if (!childPrim)
                    {
                    if (Body != IntPtr.Zero)
                        {
                        dtmp = d.BodyGetPosition(Body);
                        return new Vector3(dtmp.X,dtmp.Y,dtmp.Z);
                        }
                    }

                return Vector3.Zero;
                }
        }

        public override Vector3 GeometricCenter
        {
            get { return Vector3.Zero; }
        }

        public override PrimitiveBaseShape Shape
        {
            set
            {
            AddChange(changes.Shape, (object)value);
            }
        }

        public override Vector3 Velocity
        {
            get
            {
                // Averate previous velocity with the new one so
                // client object interpolation works a 'little' better
                if (_zeroFlag)
                    return Vector3.Zero;

                Vector3 returnVelocity = Vector3.Zero;
                returnVelocity.X = (m_lastVelocity.X + _velocity.X)/2;
                returnVelocity.Y = (m_lastVelocity.Y + _velocity.Y)/2;
                returnVelocity.Z = (m_lastVelocity.Z + _velocity.Z)/2;
                return returnVelocity;
            }
            set
            {
                if (value.IsFinite())
                {
                    AddChange(changes.Velocity,(object)value);
                }
                else
                {
                    m_log.Warn("[PHYSICS]: Got NaN Velocity in Object");
                }
            }
        }

        public override Vector3 Torque
        {
            get
            {
                if (childPrim || !m_isphysical || Body == IntPtr.Zero)
                    return Vector3.Zero;

                return _torque;
            }

            set
            {
                if (value.IsFinite())
                   AddChange(changes.Torque, (object)value);
                else
                {
                    m_log.Warn("[PHYSICS]: Got NaN Torque in Object");
                }
            }
        }

        public override float CollisionScore
        {
            get { return m_collisionscore; }
            set { m_collisionscore = value; }
        }

        public override Quaternion Orientation
            {
            get
                {
                if (fakeori)
                    return showorientation;
                else
                    return _orientation;
                }
            set
                {
                if (QuaternionIsFinite(value))
                    {
                    showorientation = value;
                    fakeori = true;
                    AddChange(changes.Orientation, (object)value);
                    }
                else
                    m_log.Warn("[PHYSICS]: Got NaN quaternion Orientation from Scene in Object");

                }
            }

        internal static bool QuaternionIsFinite(Quaternion q)
        {
            if (Single.IsNaN(q.X) || Single.IsInfinity(q.X))
                return false;
            if (Single.IsNaN(q.Y) || Single.IsInfinity(q.Y))
                return false;
            if (Single.IsNaN(q.Z) || Single.IsInfinity(q.Z))
                return false;
            if (Single.IsNaN(q.W) || Single.IsInfinity(q.W))
                return false;
            return true;
        }

        public override Vector3 Acceleration
        {
            get { return _acceleration; }
        }


        public void SetAcceleration(Vector3 accel)
        {
            AddChange(changes.Acceleration, (object)accel); 
        }

        public override void AddForce(Vector3 force, bool pushforce)
        {
            if (force.IsFinite())
            {
                AddChange(changes.Force, (object) force);
            }
            else
            {
                m_log.Warn("[PHYSICS]: Got Invalid linear force vector from Scene in Object");
            }
            //m_log.Info("[PHYSICS]: Added Force:" + force.ToString() +  " to prim at " + Position.ToString());
        }

        public override void AddAngularForce(Vector3 force, bool pushforce)
        {
            if (force.IsFinite())
            {
                AddChange(changes.AddAngForce, (object) force);
            }
            else
            {
                m_log.Warn("[PHYSICS]: Got Invalid Angular force vector from Scene in Object");
            }
        }

        public override Vector3 RotationalVelocity
        {
            get
            {
                if (_zeroFlag)
                    return Vector3.Zero;

                if (m_rotationalVelocity.ApproxEquals(Vector3.Zero, 0.2f))
                    return Vector3.Zero;

                return m_rotationalVelocity;
            }
            set
            {
                if (value.IsFinite())
                {
                AddChange(changes.AngVelocity, (object)value);
                }
                else
                {
                    m_log.Warn("[PHYSICS]: Got NaN RotationalVelocity in Object");
                }
            }
        }

        public override void CrossingFailure()
        {
            m_crossingfailures++;
            if (m_crossingfailures > _parent_scene.geomCrossingFailuresBeforeOutofbounds)
            {
                base.RaiseOutOfBounds(_position);
                return;
            }
            else if (m_crossingfailures == _parent_scene.geomCrossingFailuresBeforeOutofbounds)
            {
                m_log.Warn("[PHYSICS]: Too many crossing failures for: " + m_primName);
            }
        }

        public override float Buoyancy
        {
            get { return m_buoyancy; }
            set { m_buoyancy = value; }
        }

        public override void link(PhysicsActor obj)
            {
            AddChange(changes.Link, obj);
            }

        public override void delink()
        {
            AddChange(changes.DeLink, null);
        }

        public override void LockAngularMotion(Vector3 axis)
        {
            // reverse the zero/non zero values for ODE.
            if (axis.IsFinite())
            {
                axis.X = (axis.X > 0) ? 1f : 0f;
                axis.Y = (axis.Y > 0) ? 1f : 0f;
                axis.Z = (axis.Z > 0) ? 1f : 0f;
                m_log.DebugFormat("[axislock]: <{0},{1},{2}>", axis.X, axis.Y, axis.Z);
                AddChange(changes.AngLock, (object)axis);
            }
            else
            {
                m_log.Warn("[PHYSICS]: Got NaN locking axis from Scene on Object");
            }
        }

        public void UpdatePositionAndVelocity(float timestep)
            {
            if (m_frozen)
                return;
            //  no lock; called from Simulate() -- if you call this from elsewhere, gotta lock or do Monitor.Enter/Exit!
            if (_parent == null)
                {
                Vector3 pv = Vector3.Zero;
                bool lastZeroFlag = _zeroFlag;
                if (Body != IntPtr.Zero && prim_geom != IntPtr.Zero) // FIXME -> or if it is a joint
                    {
                    d.Vector3 cpos = d.BodyGetPosition(Body);
                    d.Vector3 lpos = d.GeomGetPosition(prim_geom);

                    d.Matrix3 mat = d.GeomGetRotation(prim_geom); // 11.1 may have getQuaternion but calculations are identical
                    d.Quaternion ori;
                    d.QfromR(out ori, ref mat);

                    d.Vector3 vel = d.BodyGetLinearVel(Body);
                    d.Vector3 rotvel = d.BodyGetAngularVel(Body);

                    m_lastposition = _position;
                    m_lastorientation = _orientation;

                    Vector3 l_position;
                    l_position.X = (float)lpos.X;
                    l_position.Y = (float)lpos.Y;
                    l_position.Z = (float)lpos.Z;
                    Quaternion l_orientation;
                    l_orientation.X = (float)ori.X;
                    l_orientation.Y = (float)ori.Y;
                    l_orientation.Z = (float)ori.Z;
                    l_orientation.W = (float)ori.W;

                    if (cpos.X > ((int)_parent_scene.WorldExtents.X - 0.05f) || 
                        cpos.X < 0f ||
                        cpos.Y > ((int)_parent_scene.WorldExtents.Y - 0.05f) || 
                        cpos.Y < 0f)
                        {
                        //base.RaiseOutOfBounds(l_position);

                        if (m_crossingfailures < _parent_scene.geomCrossingFailuresBeforeOutofbounds)
                            {
                            _position = l_position;
                            m_crossingfailures++;
                            base.RequestPhysicsterseUpdate();
                            return;
                            }
                        else
                            {
                            m_frozen = true;
                            base.RaiseOutOfBounds(l_position);
                            return;
                            }
                        }
                    #region
                    if (cpos.Z < 0 ||
                        (cpos.Z > _parent_scene.m_flightCeilingHeight && _parent_scene.m_useFlightCeilingHeight))
                        {
                        // This is so prim that get lost underground don't fall forever and suck up
                        //
                        // Sim resources and memory.
                        // Disables the prim's movement physics....
                        // It's a hack and will generate a console message if it fails.

                        //IsPhysical = false;
                        base.RaiseOutOfBounds(_position);

                        if (cpos.Z < 0)
                            cpos.Z = 0;  // put it somewhere
                        else
                            cpos.Z = _parent_scene.m_flightCeilingHeight;

                        _acceleration.X = 0;
                        _acceleration.Y = 0;
                        _acceleration.Z = 0;

                        _velocity.X = 0;
                        _velocity.Y = 0;
                        _velocity.Z = 0;
                        m_rotationalVelocity.X = 0;
                        m_rotationalVelocity.Y = 0;
                        m_rotationalVelocity.Z = 0;

                        d.BodySetLinearVel(Body, 0, 0, 0); // stop it
                        d.BodySetAngularVel(Body, 0, 0, 0); // stop it
                        d.BodySetPosition(Body, cpos.X, cpos.Y, cpos.Z); // put it somewhere 

                        base.RequestPhysicsterseUpdate();

                        m_throttleUpdates = false;
                        throttleCounter = 0;
                        _zeroFlag = true;
                        m_frozen = true;
                        }
                    #endregion

                    if ((Math.Abs(m_lastposition.X - l_position.X) < 0.001)
                        && (Math.Abs(m_lastposition.Y - l_position.Y) < 0.001)
                        && (Math.Abs(m_lastposition.Z - l_position.Z) < 0.001)
                        && (Math.Abs(vel.X) < 0.001)
                        && (Math.Abs(vel.Y) < 0.001)
                        && (Math.Abs(vel.Z) < 0.001)
                        && (Math.Abs(rotvel.X) < 0.0001)
                        && (Math.Abs(rotvel.Y) < 0.0001)
                        && (Math.Abs(rotvel.Z) < 0.0001)

                        && m_vehicle.Type == Vehicle.TYPE_NONE )
                        {
                        _zeroFlag = true;
                        m_throttleUpdates = false;
                        }
                    else
                        {
                        _zeroFlag = false;
                        m_lastUpdateSent = 1;
                        }

                    if (_zeroFlag)
                        {
                        _velocity.X = 0.0f;
                        _velocity.Y = 0.0f;
                        _velocity.Z = 0.0f;

                        _acceleration.X = 0;
                        _acceleration.Y = 0;
                        _acceleration.Z = 0;

                        m_rotationalVelocity.X = 0;
                        m_rotationalVelocity.Y = 0;
                        m_rotationalVelocity.Z = 0;

                        d.BodySetLinearVel(Body, 0, 0, 0);
                        d.BodySetAngularVel(Body, 0, 0, 0);

                        if (m_lastUpdateSent > 0)
                            {
                            if (throttleCounter > 100 || m_lastUpdateSent >= 2)
                                {
                                base.RequestPhysicsterseUpdate();
                                m_lastUpdateSent--;

                                throttleCounter = 0;
                                }
                            else
                                throttleCounter++;
                             }
                        }
                    else
                        {
                        m_lastVelocity = _velocity;
                        if (m_vehicle.Type == Vehicle.TYPE_NONE)
                            {
                            _position = l_position;

                            _velocity.X = (float)vel.X;
                            _velocity.Y = (float)vel.Y;
                            _velocity.Z = (float)vel.Z;

                            _acceleration = ((_velocity - m_lastVelocity) / timestep);
                            //m_log.Info("[PHYSICS]: V1: " + _velocity + " V2: " + m_lastVelocity + " Acceleration: " + _acceleration.ToString());

                            m_lastRotationalVelocity = m_rotationalVelocity;
                            m_rotationalVelocity = new Vector3((float)rotvel.X, (float)rotvel.Y, (float)rotvel.Z);

                            }
                        _orientation.X = (float)ori.X;
                        _orientation.Y = (float)ori.Y;
                        _orientation.Z = (float)ori.Z;
                        _orientation.W = (float)ori.W;
                        if (!m_throttleUpdates || throttleCounter > _parent_scene.geomUpdatesPerThrottledUpdate)
                            {
                            base.RequestPhysicsterseUpdate();
                            }
                        else
                            throttleCounter++;
                        }
                    }
                else
                    {
                    // Not a body..   so Make sure the client isn't interpolating
                    _velocity.X = 0;
                    _velocity.Y = 0;
                    _velocity.Z = 0;

                    _acceleration.X = 0;
                    _acceleration.Y = 0;
                    _acceleration.Z = 0;

                    m_rotationalVelocity.X = 0;
                    m_rotationalVelocity.Y = 0;
                    m_rotationalVelocity.Z = 0;
                    _zeroFlag = true;
                    m_frozen = true;
                    }
                }
            }


        public override bool FloatOnWater
            {
            set
                {
                AddChange(changes.CollidesWater, (object)value);
                }
            }

        public override void SetMomentum(Vector3 momentum)
        {
        }

        public override Vector3 PIDTarget 
        {
            get
            {
                return m_PIDTarget;
            }
            set
            {
                if (value.IsFinite())
                {
                    m_PIDTarget = value;
                }
                else
                    m_log.Warn("[PHYSICS]: Got NaN PIDTarget from Scene on Object");
            } 
        }
        public override bool PIDActive { get { return m_usePID; } set { m_usePID = value; } }
        public override float PIDTau { get { return m_PIDTau; } set { m_PIDTau = value; } }

        public override float PIDHoverHeight { set { m_PIDHoverHeight = value; ; } }
        public override bool PIDHoverActive { set { m_useHoverPID = value; } }
        public override PIDHoverType PIDHoverType { set { m_PIDHoverType = value; } }
        public override float PIDHoverTau { set { m_PIDHoverTau = value; } }
        
        public override Quaternion APIDTarget{ set { return; } }

        public override bool APIDActive{ set { return; } }

        public override float APIDStrength{ set { return; } }

        public override float APIDDamping{ set { return; } }


        private void createAMotor(Vector3 axis)
        {
            if (Body == IntPtr.Zero)
                return;

            if (Amotor != IntPtr.Zero)
            {
                d.JointDestroy(Amotor);
                Amotor = IntPtr.Zero;
            }

            int axisnum = 3 - (int)(axis.X + axis.Y + axis.Z);

            if (axisnum <= 0)
                return; 

            // stop it
            d.BodySetTorque(Body, 0, 0, 0);
            d.BodySetAngularVel(Body, 0, 0, 0);

            Amotor = d.JointCreateAMotor(_parent_scene.world, IntPtr.Zero);
            d.JointAttach(Amotor, Body, IntPtr.Zero);
           
            d.JointSetAMotorMode(Amotor, 0);
            d.JointSetAMotorNumAxes(Amotor, axisnum);

            // get current orientation to lock

            d.Quaternion dcur = d.BodyGetQuaternion(Body);
            Quaternion curr; // crap convertion between identical things
            curr.X = dcur.X;
            curr.Y = dcur.Y;
            curr.Z = dcur.Z;
            curr.W = dcur.W;
            Vector3 ax;

            int i = 0;
            int j = 0;
            if (axis.X == 0)
                {
                ax = (new Vector3(1, 0, 0)) * curr; // rotate world X to current local X
                // ODE should do this  with axis relative to body 1 but seems to fail
                d.JointSetAMotorAxis(Amotor, 0, 0, ax.X, ax.Y, ax.Z);
                d.JointSetAMotorAngle(Amotor, 0, 0);
                d.JointSetAMotorParam(Amotor, (int)dParam.LowStop, -0.001f);
                d.JointSetAMotorParam(Amotor, (int)dParam.HiStop, 0.001f);
                d.JointSetAMotorParam(Amotor, (int)dParam.Vel, 0);
                d.JointSetAMotorParam(Amotor, (int)dParam.FudgeFactor, 0.1f);
                d.JointSetAMotorParam(Amotor, (int)dParam.Bounce, 0f);
                d.JointSetAMotorParam(Amotor, (int)dParam.FMax, 55000000);
                i++;
                j = 256; // aodeplugin.cs doesn't have all parameters so this moves to next axis set
                }

            if (axis.Y == 0)
                {
                ax = (new Vector3(0, 1, 0)) * curr;
                d.JointSetAMotorAxis(Amotor, i, 0, ax.X, ax.Y, ax.Z);
                d.JointSetAMotorAngle(Amotor, i, 0);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.LowStop, -0.001f);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.HiStop, 0.001f);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.Vel, 0);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.FudgeFactor, 0.1f);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.Bounce, 0f);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.FMax, 55000000);
                i++;
                j += 256;
                }

            if (axis.Z == 0)
                {
                ax = (new Vector3(0, 0, 1)) * curr;
                d.JointSetAMotorAxis(Amotor, i, 0, ax.X, ax.Y, ax.Z);
                d.JointSetAMotorAngle(Amotor, i, 0);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.LowStop, -0.001f);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.HiStop, 0.001f);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.Vel, 0);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.FudgeFactor, 0.1f);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.Bounce, 0f);
                d.JointSetAMotorParam(Amotor, j + (int)dParam.FMax, 55000000);
                }

            d.JointAddAMotorTorques(Amotor, 0, 0, 0);
        }

        public Matrix4 FromDMass(d.Mass pMass)
        {
            Matrix4 obj;
            obj.M11 = (float)pMass.I.M00;
            obj.M12 = (float)pMass.I.M01;
            obj.M13 = (float)pMass.I.M02;
            obj.M14 = 0;
            obj.M21 = (float)pMass.I.M10;
            obj.M22 = (float)pMass.I.M11;
            obj.M23 = (float)pMass.I.M12;
            obj.M24 = 0;
            obj.M31 = (float)pMass.I.M20;
            obj.M32 = (float)pMass.I.M21;
            obj.M33 = (float)pMass.I.M22;
            obj.M34 = 0;
            obj.M41 = 0;
            obj.M42 = 0;
            obj.M43 = 0;
            obj.M44 = 1;
            return obj;
        }

        public d.Mass FromMatrix4(Matrix4 pMat, ref d.Mass obj)
        {
            obj.I.M00 = pMat[0, 0];
            obj.I.M01 = pMat[0, 1];
            obj.I.M02 = pMat[0, 2];
            obj.I.M10 = pMat[1, 0];
            obj.I.M11 = pMat[1, 1];
            obj.I.M12 = pMat[1, 2];
            obj.I.M20 = pMat[2, 0];
            obj.I.M21 = pMat[2, 1];
            obj.I.M22 = pMat[2, 2];
            return obj;
        }

        public override void SubscribeEvents(int ms)
        {
            m_eventsubscription = ms;
            _parent_scene.addCollisionEventReporting(this);
        }

        public override void UnSubscribeEvents()
        {
            _parent_scene.remCollisionEventReporting(this);
            m_eventsubscription = 0;
        }

        public void AddCollisionEvent(uint CollidedWith, ContactPoint contact)
        {
            if (CollisionEventsThisFrame == null)
                CollisionEventsThisFrame = new CollisionEventUpdate();
            CollisionEventsThisFrame.addCollider(CollidedWith, contact);
        }

        public void SendCollisions()
        {
            if (CollisionEventsThisFrame == null)
                return;

            base.SendCollisionUpdate(CollisionEventsThisFrame);

            if (CollisionEventsThisFrame.m_objCollisionList.Count == 0)
                CollisionEventsThisFrame = null;
            else
                CollisionEventsThisFrame = new CollisionEventUpdate();
        }

        public override bool SubscribedEvents()
        {
            if (m_eventsubscription > 0)
                return true;
            return false;
        }

        
        private static void DMassCopy(ref d.Mass src, ref d.Mass dst)
        {
            dst.c.W = src.c.W;
            dst.c.X = src.c.X;
            dst.c.Y = src.c.Y;
            dst.c.Z = src.c.Z;
            dst.mass = src.mass;
            dst.I.M00 = src.I.M00;
            dst.I.M01 = src.I.M01;
            dst.I.M02 = src.I.M02;
            dst.I.M10 = src.I.M10;
            dst.I.M11 = src.I.M11;
            dst.I.M12 = src.I.M12;
            dst.I.M20 = src.I.M20;
            dst.I.M21 = src.I.M21;
            dst.I.M22 = src.I.M22;
        }

        public override void SetMaterial(int pMaterial)
        {
            m_material = pMaterial;
        }

        private static void DMassDup(ref d.Mass src, out d.Mass dst)
            {
            dst = new d.Mass { };
            
            dst.c.W = src.c.W;
            dst.c.X = src.c.X;
            dst.c.Y = src.c.Y;
            dst.c.Z = src.c.Z;
            dst.mass = src.mass;
            dst.I.M00 = src.I.M00;
            dst.I.M01 = src.I.M01;
            dst.I.M02 = src.I.M02;
            dst.I.M10 = src.I.M10;
            dst.I.M11 = src.I.M11;
            dst.I.M12 = src.I.M12;
            dst.I.M20 = src.I.M20;
            dst.I.M21 = src.I.M21;
            dst.I.M22 = src.I.M22;
            }

        private void changeacceleration(Object arg)
            {
            _acceleration = (Vector3)arg;
            }

        private void changeangvelocity(Object arg)
            {
            m_rotationalVelocity = (Vector3)arg;
            }

        private void changeforce(Object arg)
            {
            m_force = (Vector3)arg;
            }

        private void changevoldtc(Object arg)
            {
            m_isVolumeDetect = ((int)arg != 0);
            }                       

        private void donullchange()
            {
            }

        public bool DoAChange(changes what, object arg)
            {
            if (m_frozen && what != changes.Add && what != changes.Remove)
                return false;

            if (prim_geom == IntPtr.Zero && what != changes.Add && what != changes.Remove)
                {
                m_frozen = true;
                return false;
                }            

            // nasty switch
            switch (what)
                {
                case changes.Add:
                    changeadd();
                    break;
                case changes.Remove:
                    if (_parent != null)
                        {
                        AuroraODEPrim parent = (AuroraODEPrim)_parent;
                        parent.ChildRemove(this);
                        }
                    else
                        ChildRemove(this);
                    return true;                  

                case changes.Link:
                    AuroraODEPrim tmp = (AuroraODEPrim)arg;
                    changelink(tmp);
                    break;

                case changes.DeLink:
                    changelink(null);
                    break;

                case changes.Position:
                    changemoveandrotate((Vector3)arg,_orientation);
                    break;

                case changes.Orientation:
                    changemoveandrotate(_position,(Quaternion)arg);
                    break;

                case changes.PosOffset:
                    donullchange();
                    break;

                case changes.OriOffset:
                    donullchange();
                    break;

                case changes.Velocity:
                    changevelocity(arg);
                    break;

                case changes.Acceleration:
                    changeacceleration(arg);
                    break;
                case changes.AngVelocity:
                    changeangvelocity(arg);
                    break;

                case changes.Force:
                    changeforce(arg);
                    break;

                case changes.Torque:
                    changeSetTorque(arg);
                    break;

                case changes.AddForce:
                    changeAddForce(arg);
                    break;

                case changes.AddAngForce:
                    changeAddAngularForce(arg);
                    break;

                case changes.AngLock:
                    changeAngularLock(arg);
                    break;

                case changes.Size:
                    changesize(arg);
                    break;

                case changes.Shape:
                    changeshape(arg);
                    break;

                case changes.CollidesWater:
                    changefloatonwater(arg);
                    break;

                case changes.VolumeDtc:
                    changevoldtc(arg);
                    break;

                case changes.Physical:
                    changePhysicsStatus((bool)arg);
                    break;

                case changes.Selected:
                    changeSelectedStatus((bool)arg);
                    break;

                case changes.disabled:
                    changedisable();
                    break;

                case changes.Null:
                    donullchange();
                    break;

                default:
                    donullchange();
                    break;
                }           
            return false;
            }

        public void AddChange(changes what, object arg)
            {
            _parent_scene.AddChange(this,what,arg);
            }
    }
}
