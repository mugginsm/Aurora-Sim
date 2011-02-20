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
using System.IO;
using System.Reflection;
using log4net;
using NDesk.Options;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Aurora.Simulation.Base;

namespace OpenSim.Region.CoreModules.Avatar.Inventory.Archiver
{
    /// <summary>
    /// This module loads and saves OpenSimulator inventory archives
    /// </summary>
    public class InventoryArchiverModule : IService, IInventoryArchiverModule
    {
        public string Name { get { return "Inventory Archiver Module"; } }
        
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        
        public event InventoryArchiveSaved OnInventoryArchiveSaved;
        
        /// <summary>
        /// The file to load and save inventory if no filename has been specified
        /// </summary>
        protected const string DEFAULT_INV_BACKUP_FILENAME = "user-inventory.iar";

        /// <value>
        /// Pending save completions initiated from the console
        /// </value>
        protected List<Guid> m_pendingConsoleSaves = new List<Guid>();
        
        /// <value>
        /// All scenes that this module knows about
        /// </value>
        private Dictionary<UUID, Scene> m_scenes = new Dictionary<UUID, Scene>();
        private IRegistryCore m_registry;
        
        /// <summary>
        /// Trigger the inventory archive saved event.
        /// </summary>
        protected internal void TriggerInventoryArchiveSaved(
            Guid id, bool succeeded, UserAccount userInfo, string invPath, Stream saveStream, 
            Exception reportedException)
        {
            InventoryArchiveSaved handlerInventoryArchiveSaved = OnInventoryArchiveSaved;
            if (handlerInventoryArchiveSaved != null)
                handlerInventoryArchiveSaved(id, succeeded, userInfo, invPath, saveStream, reportedException);
        }
        
        public bool ArchiveInventory(
             Guid id, string firstName, string lastName, string invPath, string pass, Stream saveStream)
        {
            return ArchiveInventory(id, firstName, lastName, invPath, pass, saveStream, new Dictionary<string, object>());
        }

        public bool ArchiveInventory(
            Guid id, string firstName, string lastName, string invPath, string pass, Stream saveStream, 
            Dictionary<string, object> options)
        {
            UserAccount userInfo = GetUserInfo(firstName, lastName, pass);

            if (userInfo != null)
            {
                try
                {
                    bool UseAssets = true;
                    if (options.ContainsKey("assets"))
                    {
                        object Assets = null;
                        options.TryGetValue("assets", out Assets);
                        bool.TryParse(Assets.ToString(), out UseAssets);
                    }
                    new InventoryArchiveWriteRequest(id, this, m_registry, userInfo, invPath, saveStream, UseAssets, null, new List<AssetBase>()).Execute();
                }
                catch (EntryPointNotFoundException e)
                {
                    m_log.ErrorFormat(
                        "[ARCHIVER]: Mismatch between Mono and zlib1g library version when trying to create compression stream."
                            + "If you've manually installed Mono, have you appropriately updated zlib1g as well?");
                    m_log.Error(e);

                    return false;
                }

                return true;
            }

            return false;
        }
                        
        public bool ArchiveInventory(
            Guid id, string firstName, string lastName, string invPath, string pass, string savePath, 
            Dictionary<string, object> options)
        {
            UserAccount userInfo = GetUserInfo(firstName, lastName, pass);

            if (userInfo != null)
            {
                try
                {
                    bool UseAssets = true;
                    if (options.ContainsKey("assets"))
                    {
                        object Assets = null;
                        options.TryGetValue("assets", out Assets);
                        bool.TryParse(Assets.ToString(), out UseAssets);
                    }
                    new InventoryArchiveWriteRequest(id, this, m_registry, userInfo, invPath, savePath, UseAssets).Execute();
                }
                catch (EntryPointNotFoundException e)
                {
                    m_log.ErrorFormat(
                        "[ARCHIVER]: Mismatch between Mono and zlib1g library version when trying to create compression stream."
                            + "If you've manually installed Mono, have you appropriately updated zlib1g as well?");
                    m_log.Error(e);

                    return false;
                }

                return true;
            }
            
            return false;
        }

        public bool DearchiveInventory(string firstName, string lastName, string invPath, string pass, Stream loadStream)
        {
            return DearchiveInventory(firstName, lastName, invPath, pass, loadStream, new Dictionary<string, object>());
        }
        
        public bool DearchiveInventory(
            string firstName, string lastName, string invPath, string pass, Stream loadStream, 
            Dictionary<string, object> options)
        {
            UserAccount userInfo = GetUserInfo(firstName, lastName, pass);

            if (userInfo != null)
            {
                InventoryArchiveReadRequest request;
                bool merge = (options.ContainsKey("merge") ? (bool)options["merge"] : false);

                try
                {
                    request = new InventoryArchiveReadRequest(m_registry, userInfo, invPath, loadStream, merge);
                }
                catch (EntryPointNotFoundException e)
                {
                    m_log.ErrorFormat(
                        "[ARCHIVER]: Mismatch between Mono and zlib1g library version when trying to create compression stream."
                            + "If you've manually installed Mono, have you appropriately updated zlib1g as well?");
                    m_log.Error(e);

                    return false;
                }

                request.Execute(false);

                return true;
            }

            return false;
        }
        
        public bool DearchiveInventory(
             string firstName, string lastName, string invPath, string pass, string loadPath, 
             Dictionary<string, object> options)
        {
            UserAccount userInfo = GetUserInfo(firstName, lastName, pass);

            if (userInfo != null)
            {
                InventoryArchiveReadRequest request;
                bool merge = (options.ContainsKey("merge") ? (bool)options["merge"] : false);

                try
                {
                    request = new InventoryArchiveReadRequest(m_registry, userInfo, invPath, loadPath, merge);
                }
                catch (EntryPointNotFoundException e)
                {
                    m_log.ErrorFormat(
                        "[ARCHIVER]: Mismatch between Mono and zlib1g library version when trying to create compression stream."
                            + "If you've manually installed Mono, have you appropriately updated zlib1g as well?");
                    m_log.Error(e);

                    return false;
                }

                request.Execute(false);

                return true;
            }

            return false;
        }
        
        /// <summary>
        /// Load inventory from an inventory file archive
        /// </summary>
        /// <param name="cmdparams"></param>
        protected void HandleLoadInvConsoleCommand(string module, string[] cmdparams)
        {
            try
            {
                m_log.Info("[INVENTORY ARCHIVER]: PLEASE NOTE THAT THIS FACILITY IS EXPERIMENTAL.  BUG REPORTS WELCOME.");
                
                Dictionary<string, object> options = new Dictionary<string, object>();
                OptionSet optionSet = new OptionSet().Add("m|merge", delegate (string v) { options["merge"] = v != null; });
                
                List<string> mainParams = optionSet.Parse(cmdparams);
                
                if (mainParams.Count < 6)
                {
                    m_log.Error(
                        "[INVENTORY ARCHIVER]: usage is load iar [--merge] <first name> <last name> <inventory path> <user password> [<load file path>]");
                    return;
                }
    
                string firstName = mainParams[2];
                string lastName = mainParams[3];
                string invPath = mainParams[4];
                string pass = mainParams[5];
                string loadPath = (mainParams.Count > 6 ? mainParams[6] : DEFAULT_INV_BACKUP_FILENAME);
    
                m_log.InfoFormat(
                    "[INVENTORY ARCHIVER]: Loading archive {0} to inventory path {1} for {2} {3}",
                    loadPath, invPath, firstName, lastName);
                
                if (DearchiveInventory(firstName, lastName, invPath, pass, loadPath, options))
                    m_log.InfoFormat(
                        "[INVENTORY ARCHIVER]: Loaded archive {0} for {1} {2}",
                        loadPath, firstName, lastName);
            }
            catch (InventoryArchiverException e)
            {
                m_log.ErrorFormat("[INVENTORY ARCHIVER]: {0}", e.Message);
            }
        }

            /// <summary>
        /// Save inventory to a file archive
        /// </summary>
        /// <param name="cmdparams"></param>
        protected void HandleSaveInvWOAssetsConsoleCommand(string module, string[] cmdparams)
        {
            if (cmdparams.Length < 7)
            {
                m_log.Error(
                    "[INVENTORY ARCHIVER]: usage is save iar <first name> <last name> <inventory path> <user password> [<save file path>]");
                return;
            }

            m_log.Info("[INVENTORY ARCHIVER]: PLEASE NOTE THAT THIS FACILITY IS EXPERIMENTAL.  BUG REPORTS WELCOME.");

            string firstName = cmdparams[3];
            string lastName = cmdparams[4];
            string invPath = cmdparams[5];
            string pass = cmdparams[6];
            string savePath = (cmdparams.Length > 7 ? cmdparams[7] : DEFAULT_INV_BACKUP_FILENAME);

            m_log.InfoFormat(
                "[INVENTORY ARCHIVER]: Saving archive {0} using inventory path {1} for {2} {3} without assets",
                savePath, invPath, firstName, lastName);

            Guid id = Guid.NewGuid();
            Dictionary<string,object> options = new Dictionary<string, object>();
            options.Add("Assets", false);
            ArchiveInventory(id, firstName, lastName, invPath, pass, savePath, options);

            lock (m_pendingConsoleSaves)
                m_pendingConsoleSaves.Add(id);
        }
        
        /// <summary>
        /// Save inventory to a file archive
        /// </summary>
        /// <param name="cmdparams"></param>
        protected void HandleSaveInvConsoleCommand(string module, string[] cmdparams)
        {
            try
            {
                m_log.Info("[INVENTORY ARCHIVER]: PLEASE NOTE THAT THIS FACILITY IS EXPERIMENTAL.  BUG REPORTS WELCOME.");

                string firstName = cmdparams[2];
                string lastName = cmdparams[3];
                string invPath = cmdparams[4];
                string pass = cmdparams[5];
                string savePath = (cmdparams.Length > 6 ? cmdparams[6] : DEFAULT_INV_BACKUP_FILENAME);

                m_log.InfoFormat(
                    "[INVENTORY ARCHIVER]: Saving archive {0} using inventory path {1} for {2} {3}",
                    savePath, invPath, firstName, lastName);

                Guid id = Guid.NewGuid();

                Dictionary<string, object> options = new Dictionary<string, object>();
                options.Add("Assets", true);
                ArchiveInventory(id, firstName, lastName, invPath, pass, savePath, options);

                lock (m_pendingConsoleSaves)
                    m_pendingConsoleSaves.Add(id);
            }
            catch (InventoryArchiverException e)
            {
                m_log.ErrorFormat("[INVENTORY ARCHIVER]: {0}", e.Message);
            }
        }
        
        private void SaveInvConsoleCommandCompleted(
            Guid id, bool succeeded, UserAccount userInfo, string invPath, Stream saveStream, 
            Exception reportedException)
        {
            lock (m_pendingConsoleSaves)
            {
                if (m_pendingConsoleSaves.Contains(id))
                    m_pendingConsoleSaves.Remove(id);
                else
                    return;
            }
            
            if (succeeded)
            {
                m_log.InfoFormat("[INVENTORY ARCHIVER]: Saved archive for {0} {1}", userInfo.FirstName, userInfo.LastName);
            }
            else
            {
                m_log.ErrorFormat(
                    "[INVENTORY ARCHIVER]: Archive save for {0} {1} failed - {2}", 
                    userInfo.FirstName, userInfo.LastName, reportedException.Message);
            }
        }
        
        /// <summary>
        /// Get user information for the given name.
        /// </summary>
        /// <param name="firstName"></param>
        /// <param name="lastName"></param>
        /// <param name="pass">User password</param>
        /// <returns></returns>
        protected UserAccount GetUserInfo(string firstName, string lastName, string pass)
        {
            UserAccount account 
                = m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccount(UUID.Zero, firstName, lastName);
            
            if (null == account)
            {
                m_log.ErrorFormat(
                    "[INVENTORY ARCHIVER]: Failed to find user info for {0} {1}", 
                    firstName, lastName);
                return null;
            }

            try
            {
                string encpass = Util.Md5Hash(pass);
                if (m_registry.RequestModuleInterface<IAuthenticationService>().Authenticate(account.PrincipalID, encpass, 1) != string.Empty)
                {
                    return account;
                }
                else
                {
                    m_log.ErrorFormat(
                        "[INVENTORY ARCHIVER]: Password for user {0} {1} incorrect.  Please try again.", 
                        firstName, lastName);
                    return null;
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[INVENTORY ARCHIVER]: Could not authenticate password, {0}", e.Message);
                return null;
            }
        }

        #region IService Members

        public void Initialize(IConfigSource config, IRegistryCore registry)
        {
            m_registry = registry;
            m_registry.RegisterModuleInterface<IInventoryArchiverModule>(this);
            if (m_scenes.Count == 0)
            {
                OnInventoryArchiveSaved += SaveInvConsoleCommandCompleted;

                MainConsole.Instance.Commands.AddCommand(
                    this.Name, true, "load iar",
                    "load iar <first> <last> <inventory path> <password> [<IAR path>]",
                    //"load iar [--merge] <first> <last> <inventory path> <password> [<IAR path>]",
                    "Load user inventory archive (IAR).",
                    //"--merge is an option which merges the loaded IAR with existing inventory folders where possible, rather than always creating new ones"
                    //+ "<first> is user's first name." + Environment.NewLine
                    "<first> is user's first name." + Environment.NewLine
                    + "<last> is user's last name." + Environment.NewLine
                    + "<inventory path> is the path inside the user's inventory where the IAR should be loaded." + Environment.NewLine
                    + "<password> is the user's password." + Environment.NewLine
                    + "<IAR path> is the filesystem path or URI from which to load the IAR."
                    + string.Format("  If this is not given then the filename {0} in the current directory is used", DEFAULT_INV_BACKUP_FILENAME),
                    HandleLoadInvConsoleCommand);

                MainConsole.Instance.Commands.AddCommand(
                    this.Name, true, "save iar",
                    "save iar <first> <last> <inventory path> <password> [<IAR path>]",
                    "Save user inventory archive (IAR).",
                    "<first> is the user's first name." + Environment.NewLine
                    + "<last> is the user's last name." + Environment.NewLine
                    + "<inventory path> is the path inside the user's inventory for the folder/item to be saved." + Environment.NewLine
                    + "<IAR path> is the filesystem path at which to save the IAR."
                    + string.Format("  If this is not given then the filename {0} in the current directory is used", DEFAULT_INV_BACKUP_FILENAME),
                    HandleSaveInvConsoleCommand);

                MainConsole.Instance.Commands.AddCommand(
                    this.Name, true, "save iar withoutassets",
                    "save iar withoutassets <first> <last> <inventory path> <password> [<IAR path>]",
                    "Save user inventory archive (IAR) withOUT assets. This version will NOT load on another grid/standalone other than the current grid/standalone!",
                    "<first> is the user's first name." + Environment.NewLine
                    + "<last> is the user's last name." + Environment.NewLine
                    + "<inventory path> is the path inside the user's inventory for the folder/item to be saved." + Environment.NewLine
                    + "<IAR path> is the filesystem path at which to save the IAR."
                    + string.Format("  If this is not given then the filename {0} in the current directory is used", DEFAULT_INV_BACKUP_FILENAME),
                    HandleSaveInvWOAssetsConsoleCommand);
            }
        }

        public void Start(IConfigSource config, IRegistryCore registry)
        {
        }

        public void FinishedStartup()
        {
        }

        #endregion
    }
}
