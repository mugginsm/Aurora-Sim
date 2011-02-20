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
using System.Net;
using System.Reflection;
using System.Threading;

using OpenSim.Framework;
using OpenSim.Framework.Capabilities;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using log4net;
using Nini.Config;

namespace OpenSim.Region.CoreModules.Framework.EntityTransfer
{
    public class EntityTransferModule : ISharedRegionModule, IEntityTransferModule
    {
        #region Declares

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected bool m_Enabled = false;
        protected List<Scene> m_scenes = new List<Scene>();
        
        #endregion

        #region ISharedRegionModule

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public virtual string Name
        {
            get { return "BasicEntityTransferModule"; }
        }

        public virtual void Initialise(IConfigSource source)
        {
            IConfig moduleConfig = source.Configs["Modules"];
            if (moduleConfig != null)
            {
                string name = moduleConfig.GetString("EntityTransferModule", "");
                if (name == Name)
                {
                    m_Enabled = true;
                    //m_log.InfoFormat("[ENTITY TRANSFER MODULE]: {0} enabled.", Name);
                }
            }
        }

        public virtual void PostInitialise()
        {
        }

        public virtual void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_scenes.Add(scene);

            scene.RegisterModuleInterface<IEntityTransferModule>(this);
            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnClosingClient += OnClosingClient;
        }

        public virtual void Close()
        {
        }

        public virtual void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;
            m_scenes.Remove(scene);

            scene.UnregisterModuleInterface<IEntityTransferModule>(this);
            scene.EventManager.OnNewClient -= OnNewClient;
            scene.EventManager.OnClosingClient -= OnClosingClient;
        }

        public virtual void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

        }


        #endregion

        #region Agent Teleports

        public virtual void Teleport(ScenePresence sp, ulong regionHandle, Vector3 position, Vector3 lookAt, uint teleportFlags)
        {
            uint x = 0, y = 0;
            Utils.LongToUInts(regionHandle, out x, out y);

            GridRegion reg = sp.Scene.GridService.GetRegionByPosition(sp.Scene.RegionInfo.ScopeID, (int)x, (int)y);

            if (reg == null)
            {
                // TP to a place that doesn't exist (anymore)
                // Inform the viewer about that
                sp.ControllingClient.SendTeleportFailed("The region you tried to teleport to doesn't exist anymore");

                // and set the map-tile to '(Offline)'
                uint regX, regY;
                Utils.LongToUInts(regionHandle, out regX, out regY);

                MapBlockData block = new MapBlockData();
                block.X = (ushort)(regX / Constants.RegionSize);
                block.Y = (ushort)(regY / Constants.RegionSize);
                block.Access = 254; // == not there

                List<MapBlockData> blocks = new List<MapBlockData>();
                blocks.Add(block);
                sp.ControllingClient.SendMapBlock(blocks, 0);
                return;
            }
            Teleport(sp, reg, position, lookAt, teleportFlags);
        }

        public virtual void Teleport(ScenePresence sp, GridRegion finalDestination, Vector3 position, Vector3 lookAt, uint teleportFlags)
        {
            string reason = "";

            sp.ControllingClient.SendTeleportStart(teleportFlags);
            sp.ControllingClient.SendTeleportProgress(teleportFlags, "requesting");

            // Reset animations; the viewer does that in teleports.
            if(sp.Animator != null)
                sp.Animator.ResetAnimations();

            try
            {
                long XShift = (finalDestination.RegionLocX - sp.Scene.RegionInfo.RegionLocX);
                long YShift = (finalDestination.RegionLocY - sp.Scene.RegionInfo.RegionLocY);
                if (finalDestination.RegionHandle == sp.Scene.RegionInfo.RegionHandle || //Take region size into account as well
                    (XShift < sp.Scene.RegionInfo.RegionSizeX && YShift < sp.Scene.RegionInfo.RegionSizeY &&
                    XShift > 0 && YShift > 0 && //Can't have negatively sized regions
                    sp.Scene.RegionInfo.RegionSizeX != int.MaxValue && sp.Scene.RegionInfo.RegionSizeY != int.MaxValue))
                {
                    //First check whether the user is allowed to move at all
                    if (!sp.Scene.Permissions.AllowedOutgoingLocalTeleport(sp.UUID, out reason))
                    {
                        sp.ControllingClient.SendTeleportFailed(reason);
                        return;
                    }
                    //Now respect things like parcel bans with this
                    if (!sp.Scene.Permissions.AllowedIncomingTeleport(sp.UUID, position, out position, out reason))
                    {
                        sp.ControllingClient.SendTeleportFailed(reason);
                        return;
                    }
                    m_log.DebugFormat(
                        "[ENTITY TRANSFER MODULE]: RequestTeleportToLocation {0} within {1}",
                        position, sp.Scene.RegionInfo.RegionName);

                    //We have to add the shift as it is brought into this as well in regions that have larger RegionSizes
                    position.X += XShift;
                    position.Y += YShift;

                    //Keep users from being underground
                    ITerrainChannel channel = sp.Scene.RequestModuleInterface<ITerrainChannel>();
                    float groundHeight = channel.GetNormalizedGroundHeight(position.X, position.Y);
                    if (position.Z < groundHeight)
                    {
                        position.Z = groundHeight;
                    }

                    sp.ControllingClient.SendTeleportStart(teleportFlags);

                    sp.ControllingClient.SendLocalTeleport(position, lookAt, teleportFlags);
                    sp.Teleport(position);
                }
                else // Another region possibly in another simulator
                {
                    // Make sure the user is allowed to leave this region
                    if (!sp.Scene.Permissions.AllowedOutgoingRemoteTeleport(sp.UUID, out reason))
                    {
                        sp.ControllingClient.SendTeleportFailed(reason);
                        return;
                    }
                    //m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Final destination is x={0} y={1} uuid={2}",
                    //    finalDestination.RegionLocX / Constants.RegionSize, finalDestination.RegionLocY / Constants.RegionSize, finalDestination.RegionID);

                    // Check that these are not the same coordinates
                    if (finalDestination.RegionLocX == sp.Scene.RegionInfo.RegionLocX &&
                        finalDestination.RegionLocY == sp.Scene.RegionInfo.RegionLocY)
                    {
                        // Can't do. Viewer crashes
                        sp.ControllingClient.SendTeleportFailed("Space warp! You would crash. Move to a different region and try again.");
                        return;
                    }

                    //
                    // This is it
                    //
                    DoTeleport(sp, finalDestination, position, lookAt, teleportFlags);
                    //
                    //
                    //
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[ENTITY TRANSFER MODULE]: Exception on teleport: {0}\n{1}", e.Message, e.StackTrace);
                sp.ControllingClient.SendTeleportFailed("Internal error");
            }
        }

        public virtual void DoTeleport(ScenePresence sp, GridRegion finalDestination, Vector3 position, Vector3 lookAt, uint teleportFlags)
        {
            sp.ControllingClient.SendTeleportProgress(teleportFlags, "sending_dest");
            if (finalDestination == null)
            {
                sp.ControllingClient.SendTeleportFailed("Unable to locate destination");
                return;
            }

            m_log.DebugFormat(
                "[ENTITY TRANSFER MODULE]: Request Teleport to {0}:{1}/{2}",
                finalDestination.ServerURI, finalDestination.RegionName, position);

            int newRegionX = finalDestination.RegionLocX;
            int newRegionY = finalDestination.RegionLocY;
            int oldRegionX = sp.Scene.RegionInfo.RegionLocX;
            int oldRegionY = sp.Scene.RegionInfo.RegionLocY;

            sp.ControllingClient.SendTeleportProgress(teleportFlags, "arriving");

            // Fixing a bug where teleporting while sitting results in the avatar ending up removed from
            // both regions
            if (sp.ParentID != UUID.Zero)
                sp.StandUp();

            //Make sure that all attachments are ready for the teleport
            IAttachmentsModule attModule = sp.Scene.RequestModuleInterface<IAttachmentsModule>();
            if(attModule != null)
                attModule.ValidateAttachments(sp.UUID);

            AgentCircuitData agentCircuit = sp.ControllingClient.RequestClientInfo();
            agentCircuit.startpos = position;
            //The agent will be a root agent
            agentCircuit.child = false;
            //Make sure the appearnace is right
            agentCircuit.Appearance = sp.Appearance;

            AgentData agent = new AgentData();
            sp.CopyTo(agent);
            //Fix the position
            agent.Position = position;

            IEventQueueService eq = sp.Scene.RequestModuleInterface<IEventQueueService>();
            if (eq != null)
            {
                ISyncMessagePosterService syncPoster = sp.Scene.RequestModuleInterface<ISyncMessagePosterService>();
                if (syncPoster != null)
                {
                    //This does CreateAgent and sends the EnableSimulator/EstablishAgentCommunication/TeleportFinish
                    //  messages if they need to be called and deals with the callback
                    OSDMap map = syncPoster.Get(SyncMessageHelper.TeleportAgent((int)sp.DrawDistance,
                        agentCircuit, agent, teleportFlags, finalDestination, sp.Scene.RegionInfo.RegionHandle));
                    if (!map.ContainsKey("Success") || !map["Success"].AsBoolean())
                    {
                        // Fix the agent status
                        sp.IsChildAgent = false;
                        sp.ControllingClient.SendTeleportFailed("Destination refused");
                        return;
                    }
                }
            }

            //Kill the groups here, otherwise they will become ghost attachments 
            //  and stay in the sim, they'll get readded below into the new sim
            KillAttachments(sp);

            // Well, this is it. The agent is over there.
            KillEntity(sp.Scene, sp);

            INeighborService service = sp.Scene.RequestModuleInterface<INeighborService>();
            if (service != null)
            {
                //Check that the region the client is in right now isn't a part of the
                //  regions that should be closed as well
                if (service.IsOutsideView(sp.Scene.RegionInfo.RegionLocX, finalDestination.RegionLocX,
                    sp.Scene.RegionInfo.RegionLocY, finalDestination.RegionLocY))
                {
                    Thread.Sleep(1000);
                    // Fix this so that when we close, we don't have the wrong type
                    sp.IsChildAgent = false;
                    //Wait a bit for the agent to leave this region, then close them
                    sp.Scene.IncomingCloseAgent(sp.UUID);
                }
                else
                    sp.MakeChildAgent();
            }
        }

        protected void KillEntity(Scene scene, ISceneEntity entity)
        {
            scene.ForEachClient(delegate(IClientAPI client)
            {
                client.SendKillObject(scene.RegionInfo.RegionHandle, new ISceneEntity[] { entity });
            });
        }

        protected void KillEntities(Scene scene, ISceneEntity[] grp)
        {
            scene.ForEachClient(delegate(IClientAPI client)
            {
                client.SendKillObject(scene.RegionInfo.RegionHandle, grp);
            });
        }

        #endregion

        #region Client Events

        protected virtual void OnNewClient(IClientAPI client)
        {
            client.OnTeleportHomeRequest += TeleportHome;
            client.OnTeleportCancel += RequestTeleportCancel;
            client.OnTeleportLocationRequest += RequestTeleportLocation;
            client.OnTeleportLandmarkRequest += RequestTeleportLandmark;
        }

        protected virtual void OnClosingClient(IClientAPI client)
        {
            client.OnTeleportHomeRequest -= TeleportHome;
            client.OnTeleportCancel -= RequestTeleportCancel;
            client.OnTeleportLocationRequest -= RequestTeleportLocation;
            client.OnTeleportLandmarkRequest -= RequestTeleportLandmark;
        }

        public void RequestTeleportCancel(IClientAPI client)
        {
            CancelTeleport(client.AgentId, client.Scene.RegionInfo.RegionHandle);
        }

        /// <summary>
        /// Tries to teleport agent to other region.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="regionName"></param>
        /// <param name="position"></param>
        /// <param name="lookAt"></param>
        /// <param name="teleportFlags"></param>
        public void RequestTeleportLocation(IClientAPI remoteClient, string regionName, Vector3 position,
                                            Vector3 lookat, uint teleportFlags)
        {
            GridRegion regionInfo = remoteClient.Scene.RequestModuleInterface<IGridService>().GetRegionByName(UUID.Zero, regionName);
            if (regionInfo == null)
            {
                // can't find the region: Tell viewer and abort
                remoteClient.SendTeleportFailed("The region '" + regionName + "' could not be found.");
                return;
            }

            RequestTeleportLocation(remoteClient, regionInfo, position, lookat, teleportFlags);
        }

        /// <summary>
        /// Tries to teleport agent to other region.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="regionHandle"></param>
        /// <param name="position"></param>
        /// <param name="lookAt"></param>
        /// <param name="teleportFlags"></param>
        public void RequestTeleportLocation(IClientAPI remoteClient, ulong regionHandle, Vector3 position,
                                            Vector3 lookAt, uint teleportFlags)
        {
            Scene scene = (Scene)remoteClient.Scene;
            ScenePresence sp = scene.GetScenePresence(remoteClient.AgentId);
            if (sp != null)
            {
                Teleport(sp, regionHandle, position, lookAt, teleportFlags);
            }
        }

        /// <summary>
        /// Tries to teleport agent to other region.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="regionHandle"></param>
        /// <param name="position"></param>
        /// <param name="lookAt"></param>
        /// <param name="teleportFlags"></param>
        public void RequestTeleportLocation(IClientAPI remoteClient, GridRegion reg, Vector3 position,
                                            Vector3 lookAt, uint teleportFlags)
        {
            Scene scene = (Scene)remoteClient.Scene;
            ScenePresence sp = scene.GetScenePresence(remoteClient.AgentId);
            if (sp != null)
            {
                Teleport(sp, reg, position, lookAt, teleportFlags);
            }
        }

        /// <summary>
        /// Tries to teleport agent to landmark.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="regionHandle"></param>
        /// <param name="position"></param>
        public void RequestTeleportLandmark(IClientAPI remoteClient, UUID regionID, Vector3 position)
        {
            GridRegion info = null;
            try
            {
                info = remoteClient.Scene.RequestModuleInterface<IGridService>().GetRegionByUUID(UUID.Zero, regionID);
            }
            catch( Exception ex)
            {
                m_log.Warn("[EntityTransferModule]: Error finding landmark's region for user " + remoteClient.Name + ", " + ex.ToString());
            }
            if (info == null)
            {
                // can't find the region: Tell viewer and abort
                remoteClient.SendTeleportFailed("The teleport destination could not be found.");
                return;
            }

            RequestTeleportLocation(remoteClient, info, position, Vector3.Zero, (uint)(TeleportFlags.SetLastToTarget | TeleportFlags.ViaLandmark));
        }

        #endregion

        #region Teleport Home

        public virtual void TeleportHome(UUID id, IClientAPI client)
        {
            //m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Request to teleport {0} {1} home", client.FirstName, client.LastName);

            //OpenSim.Services.Interfaces.PresenceInfo pinfo = m_aScene.PresenceService.GetAgent(client.SessionId);
            UserInfo uinfo = client.Scene.RequestModuleInterface<IAgentInfoService>().GetUserInfo(client.AgentId.ToString());

            if (uinfo != null)
            {
                GridRegion regionInfo = GetScene(client.Scene.RegionInfo.RegionID).GridService.GetRegionByUUID(UUID.Zero, uinfo.HomeRegionID);
                if (regionInfo == null)
                {
                    // can't find the Home region: Tell viewer and abort
                    client.SendTeleportFailed("Your home region could not be found.");
                    return;
                }
                m_log.DebugFormat("[ENTITY TRANSFER MODULE]: User's home region is {0} {1} ({2}-{3})",
                    regionInfo.RegionName, regionInfo.RegionID, regionInfo.RegionLocX / Constants.RegionSize, regionInfo.RegionLocY / Constants.RegionSize);

                RequestTeleportLocation(
                    client, regionInfo, uinfo.HomePosition, uinfo.HomeLookAt,
                    (uint)(TeleportFlags.SetLastToTarget | TeleportFlags.ViaHome));
            }
            else
            {
                //Default region time...
                List<GridRegion> Regions = GetScene(client.Scene.RegionInfo.RegionID).GridService.GetDefaultRegions(UUID.Zero);
                if (Regions.Count != 0)
                {
                    m_log.DebugFormat("[ENTITY TRANSFER MODULE]: User's home region was not found, using {0} {1} ({2}-{3})",
                        Regions[0].RegionName, Regions[0].RegionID, Regions[0].RegionLocX / Constants.RegionSize, Regions[0].RegionLocY / Constants.RegionSize);

                    RequestTeleportLocation(
                        client, Regions[0], new Vector3(128, 128, 25), new Vector3(128, 128, 128),
                        (uint)(TeleportFlags.SetLastToTarget | TeleportFlags.ViaHome));
                }
            }
        }

        #endregion

        #region Agent Crossings

        public virtual void Cross(ScenePresence agent, bool isFlying, GridRegion crossingRegion)
        {
            Scene scene = agent.Scene;
            Vector3 newposition = new Vector3(agent.AbsolutePosition.X, agent.AbsolutePosition.Y, agent.AbsolutePosition.Z);;

            CrossAgentToNewRegionDelegate d = CrossAgentToNewRegionAsync;
            d.BeginInvoke(agent, newposition, crossingRegion, isFlying, CrossAgentToNewRegionCompleted, d);
        }

        public delegate ScenePresence CrossAgentToNewRegionDelegate(ScenePresence agent, Vector3 pos, GridRegion crossingRegion, bool isFlying);

        /// <summary>
        /// This Closes child agents on neighboring regions
        /// Calls an asynchronous method to do so..  so it doesn't lag the sim.
        /// </summary>
        protected ScenePresence CrossAgentToNewRegionAsync(ScenePresence agent, Vector3 pos,
            GridRegion crossingRegion, bool isFlying)
        {
            m_log.DebugFormat("[EntityTransferModule]: Crossing agent {0} {1} to region {2}", agent.Firstname, agent.Lastname, crossingRegion.RegionName);

            Scene m_scene = agent.Scene;

            if (crossingRegion != null)
            {
                //Make sure that all attachments are ready for the teleport
                IAttachmentsModule attModule = agent.Scene.RequestModuleInterface<IAttachmentsModule>();
                if (attModule != null)
                    attModule.ValidateAttachments(agent.UUID);

                int xOffset = crossingRegion.RegionLocX - m_scene.RegionInfo.RegionLocX;
                int yOffset = crossingRegion.RegionLocY - m_scene.RegionInfo.RegionLocY;

                if (xOffset < 0)
                    pos.X += m_scene.RegionInfo.RegionSizeX;
                else if (xOffset > 0)
                    pos.X -= Constants.RegionSize;

                if (yOffset < 0)
                    pos.Y += m_scene.RegionInfo.RegionSizeY;
                else if (yOffset > 0)
                    pos.Y -= Constants.RegionSize;

                //Make sure that they are within bounds (velocity can push it out of bounds)
                if (pos.X < 0)
                    pos.X = 1;
                if (pos.Y < 0)
                    pos.Y = 1;

                if (pos.X > crossingRegion.RegionSizeX)
                    pos.X = crossingRegion.RegionSizeX - 1;
                if (pos.Y > crossingRegion.RegionSizeY)
                    pos.Y = crossingRegion.RegionSizeY - 1;

                AgentData cAgent = new AgentData();
                agent.CopyTo(cAgent);
                cAgent.Position = pos;
                if (isFlying)
                    cAgent.ControlFlags |= (uint)AgentManager.ControlFlags.AGENT_CONTROL_FLY;

                AgentCircuitData agentCircuit = agent.ControllingClient.RequestClientInfo();
                agentCircuit.startpos = pos;
                agentCircuit.child = false;
                agentCircuit.Appearance = agent.Appearance;

                IEventQueueService eq = agent.Scene.RequestModuleInterface<IEventQueueService>();
                if (eq != null)
                {
                    //This does UpdateAgent and closing of child agents
                    //  messages if they need to be called
                    ISyncMessagePosterService syncPoster = agent.Scene.RequestModuleInterface<ISyncMessagePosterService>();
                    if (syncPoster != null)
                    {
                        OSDMap map = syncPoster.Get(SyncMessageHelper.CrossAgent(crossingRegion, pos,
                            agent.Velocity, agentCircuit, cAgent, agent.Scene.RegionInfo.RegionHandle));
                        if (!map.ContainsKey("Success") || !map["Success"].AsBoolean())
                        {
                            agent.ControllingClient.SendTeleportFailed("Could not cross");
                            return agent;
                        }
                    }
                }
                
                agent.MakeChildAgent();
                //Revolution- We already were in this region... we don't need updates about the avatars we already know about, right?
                // now we have a child agent in this region. Request and send all interesting data about (root) agents in the sim
                //agent.SendOtherAgentsAvatarDataToMe();
                //agent.SendOtherAgentsAppearanceToMe();

                //Kill the groups here, otherwise they will become ghost attachments 
                //  and stay in the sim, they'll get readded below into the new sim
                KillAttachments(agent);
            }
            return agent;
        }

        private void KillAttachments(ScenePresence agent)
        {
            IAttachmentsModule attModule = agent.Scene.RequestModuleInterface<IAttachmentsModule>();
            if (attModule != null)
            {
                SceneObjectGroup[] attachments = attModule.GetAttachmentsForAvatar(agent.UUID);
                foreach (SceneObjectGroup grp in attachments)
                {
                    //Kill in all clients as it will be readded in the other region
                    KillEntities(agent.Scene, grp.ChildrenEntities().ToArray());
                    //Now remove it from the Scene so that it will not come back
                    agent.Scene.SceneGraph.DeleteEntity(grp);
                    //And from storage as well
                    IBackupModule backup = agent.Scene.RequestModuleInterface<IBackupModule>();
                    if (backup != null)
                        backup.DeleteFromStorage(grp.UUID);
                }
            }
        }

        protected void CrossAgentToNewRegionCompleted(IAsyncResult iar)
        {
            CrossAgentToNewRegionDelegate icon = (CrossAgentToNewRegionDelegate)iar.AsyncState;
            ScenePresence agent = icon.EndInvoke(iar);

            // If the cross was successful, this agent is a child agent
            // Otherwise, put them back in the scene
            if (!agent.IsChildAgent)
            {
                bool m_flying = ((agent.AgentControlFlags & (uint)AgentManager.ControlFlags.AGENT_CONTROL_FLY) != 0);
                agent.AddToPhysicalScene(m_flying, false);
            }

            // In any case
            agent.NotInTransit();

            //m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Crossing agent {0} {1} completed.", agent.Firstname, agent.Lastname);
        }

        #endregion

        #region Object Transfers

        /// <summary>
        /// Move the given scene object into a new region depending on which region its absolute position has moved
        /// into.
        ///
        /// This method locates the new region handle and offsets the prim position for the new region
        /// </summary>
        /// <param name="attemptedPosition">the attempted out of region position of the scene object</param>
        /// <param name="grp">the scene object that we're crossing</param>
        public bool CrossGroupToNewRegion(SceneObjectGroup grp, Vector3 attemptedPosition, GridRegion destination)
        {
            if (grp == null)
                return false;
            if (grp.IsDeleted)
                return false;

            if (grp.Scene == null)
                return false;
            if (grp.RootPart.DIE_AT_EDGE)
            {
                // We remove the object here
                try
                {
                    IBackupModule backup = grp.Scene.RequestModuleInterface<IBackupModule>();
                    if (backup != null)
                        return backup.DeleteSceneObjects(new SceneObjectGroup[1] { grp }, true);
                }
                catch (Exception)
                {
                    m_log.Warn("[DATABASE]: exception when trying to remove the prim that crossed the border.");
                }
                return false;
            }

            if (grp.RootPart.RETURN_AT_EDGE)
            {
                // We remove the object here
                try
                {
                    List<SceneObjectGroup> objects = new List<SceneObjectGroup>() { grp };
                    ILLClientInventory inventoryModule = grp.Scene.RequestModuleInterface<ILLClientInventory>();
                    if (inventoryModule != null)
                        return inventoryModule.ReturnObjects(objects.ToArray(), UUID.Zero);
                }
                catch (Exception)
                {
                    m_log.Warn("[SCENE]: exception when trying to return the prim that crossed the border.");
                }
                return false;
            }

            Vector3 oldGroupPosition = grp.RootPart.GroupPosition;
            // If we fail to cross the border, then reset the position of the scene object on that border.
            if (destination != null && !CrossPrimGroupIntoNewRegion(destination, grp, attemptedPosition))
            {
                grp.OffsetForNewRegion(oldGroupPosition);
                grp.ScheduleGroupUpdate(PrimUpdateFlags.FullUpdate);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Move the given scene object into a new region
        /// </summary>
        /// <param name="newRegionHandle"></param>
        /// <param name="grp">Scene Object Group that we're crossing</param>
        /// <returns>
        /// true if the crossing itself was successful, false on failure
        /// </returns>
        protected bool CrossPrimGroupIntoNewRegion(GridRegion destination, SceneObjectGroup grp, Vector3 attemptedPos)
        {
            bool successYN = false;
            grp.RootPart.ClearUpdateScheduleOnce();
            if (destination != null)
            {
                if (grp.RootPart.SitTargetAvatar.Count != 0)
                {
                    lock (grp.RootPart.SitTargetAvatar)
                    {
                        foreach (UUID avID in grp.RootPart.SitTargetAvatar)
                        {
                            ScenePresence SP = grp.Scene.GetScenePresence(avID);
                            CrossAgentToNewRegionAsync(SP, grp.AbsolutePosition, destination, false);
                        }
                    }
                }

                SceneObjectGroup copiedGroup = (SceneObjectGroup)grp.Copy(false);
                copiedGroup.SetAbsolutePosition(true, attemptedPos);
                if (grp.Scene != null && grp.Scene.SimulationService != null)
                    successYN = grp.Scene.SimulationService.CreateObject(destination, copiedGroup);

                if (successYN)
                {
                    // We remove the object here
                    try
                    {
                        foreach (SceneObjectPart part in grp.ChildrenList)
                        {
                            lock (part.SitTargetAvatar)
                            {
                                part.SitTargetAvatar.Clear();
                            }
                        }
                        IBackupModule backup = grp.Scene.RequestModuleInterface<IBackupModule>();
                        if (backup != null)
                            return backup.DeleteSceneObjects(new SceneObjectGroup[1] { grp }, false);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[ENTITY TRANSFER MODULE]: Exception deleting the old object left behind on a border crossing for {0}, {1}",
                            grp, e);
                    }
                }
                else
                {
                    if (!grp.IsDeleted)
                    {
                        if (grp.RootPart.PhysActor != null)
                        {
                            grp.RootPart.PhysActor.CrossingFailure();
                        }
                    }

                    m_log.ErrorFormat("[ENTITY TRANSFER MODULE]: Prim crossing failed for {0}", grp);
                }
            }
            else
            {
                m_log.Error("[ENTITY TRANSFER MODULE]: destination was unexpectedly null in Scene.CrossPrimGroupIntoNewRegion()");
            }

            return successYN;
        }

        #endregion

        #region Incoming Object Transfers

        /// <summary>
        /// Attachment rezzing
        /// </summary>
        /// <param name="userID">Agent Unique ID</param>
        /// <param name="itemID">Inventory Item ID to rez</param>
        /// <returns>False</returns>
        public virtual bool IncomingCreateObject(UUID regionID, UUID userID, UUID itemID)
        {
            /*//m_log.DebugFormat(" >>> IncomingCreateObject(userID, itemID) <<< {0} {1}", userID, itemID);
            Scene scene = GetScene(regionID);
            if (scene == null)
                return false;
            ScenePresence sp = scene.GetScenePresence(userID);
            IAttachmentsModule attachMod = scene.RequestModuleInterface<IAttachmentsModule>();
            if (sp != null && attachMod != null)
            {
                m_log.DebugFormat(
                        "[EntityTransferModule]: Received attachment via new attachment method {0} for agent {1}", itemID, sp.Name);
                int attPt = sp.Appearance.GetAttachpoint(itemID);
                attachMod.RezSingleAttachmentFromInventory(sp.ControllingClient, itemID, attPt, true);
                return true;
            }*/

            return false;
        }

        /// <summary>
        /// Called when objects or attachments cross the border, or teleport, between regions.
        /// </summary>
        /// <param name="sog"></param>
        /// <returns></returns>
        public virtual bool IncomingCreateObject(UUID regionID, ISceneObject sog)
        {
            Scene scene = GetScene(regionID);
            if (scene == null)
                return false;
            
            //m_log.Debug(" >>> IncomingCreateObject(sog) <<< " + ((SceneObjectGroup)sog).AbsolutePosition + " deleted? " + ((SceneObjectGroup)sog).IsDeleted);
            SceneObjectGroup newObject;
            try
            {
                newObject = (SceneObjectGroup)sog;
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[EntityTransferModule]: Problem casting object: {0}", e.Message);
                return false;
            }

            if (!AddSceneObject(scene, newObject))
            {
                m_log.WarnFormat("[EntityTransferModule]: Problem adding scene object {0} in {1} ", sog.UUID, scene.RegionInfo.RegionName);
                return false;
            }

            newObject.RootPart.ParentGroup.CreateScriptInstances(0, false, 1, UUID.Zero);
            newObject.RootPart.ParentGroup.ResumeScripts();

            if (newObject.RootPart.SitTargetAvatar.Count != 0)
            {
                lock (newObject.RootPart.SitTargetAvatar)
                {
                    foreach (UUID avID in newObject.RootPart.SitTargetAvatar)
                    {
                        ScenePresence SP = scene.GetScenePresence(avID);
                        while (SP == null)
                        {
                            Thread.Sleep(20);
                        }
                        SP.AbsolutePosition = newObject.AbsolutePosition;
                        SP.CrossSittingAgent(SP.ControllingClient, newObject.RootPart.UUID);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Adds a Scene Object group to the Scene.
        /// Verifies that the creator of the object is not banned from the simulator.
        /// Checks if the item is an Attachment
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <returns>True if the SceneObjectGroup was added, False if it was not</returns>
        public bool AddSceneObject(Scene scene, SceneObjectGroup sceneObject)
        {
            // If the user is banned, we won't let any of their objects
            // enter. Period.
            //
            if (scene.RegionInfo.EstateSettings.IsBanned(sceneObject.OwnerID))
            {
                m_log.Info("[EntityTransferModule]: Denied prim crossing for banned avatar");

                return false;
            }

            if (!sceneObject.IsAttachmentCheckFull()) // Not Attachment
            {
                if (!scene.Permissions.CanObjectEntry(sceneObject.UUID,
                        true, sceneObject.AbsolutePosition, sceneObject.OwnerID))
                {
                    // Deny non attachments based on parcel settings
                    //
                    m_log.Info("[EntityTransferModule]: Denied prim crossing " +
                            "because of parcel settings");

                    IBackupModule backup = scene.RequestModuleInterface<IBackupModule>();
                    if (backup != null)
                        backup.DeleteSceneObjects(new SceneObjectGroup[1] { sceneObject }, true);

                    return false;
                }
                if (scene.SceneGraph.AddPrimToScene(sceneObject))
                {
                    if(sceneObject.RootPart.IsSelected)
                        sceneObject.RootPart.CreateSelected = true;
                    sceneObject.ScheduleGroupUpdate(PrimUpdateFlags.FullUpdate);
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Misc

        public void CancelTeleport(UUID AgentID, ulong RegionHandle)
        {
            ISyncMessagePosterService syncPoster = m_scenes[0].RequestModuleInterface<ISyncMessagePosterService>();
            if (syncPoster != null)
            {
                syncPoster.Post(SyncMessageHelper.CancelTeleport(AgentID, RegionHandle));
            }
        }

        /// <summary>
        /// This 'can' return null, so be careful
        /// </summary>
        /// <param name="RegionID"></param>
        /// <returns></returns>
        public Scene GetScene(UUID RegionID)
        {
            foreach (Scene scene in m_scenes)
            {
                if (scene.RegionInfo.RegionID == RegionID)
                    return scene;
            }
            return null;
        }

        /// <summary>
        /// This is pretty much guaranteed to return a Scene
        ///   as it will return the first scene if it cannot find the scene
        /// </summary>
        /// <param name="RegionID"></param>
        /// <returns></returns>
        public Scene TryGetScene(UUID RegionID)
        {
            foreach (Scene scene in m_scenes)
            {
                if (scene.RegionInfo.RegionID == RegionID)
                    return scene;
            }
            return m_scenes[0];
        }

        #endregion
    }
}
