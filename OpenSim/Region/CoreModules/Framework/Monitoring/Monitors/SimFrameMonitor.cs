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

using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.CoreModules.Framework.Monitoring.Monitors
{
    public class SimFrameMonitor : IMonitor, ISimFrameMonitor
    {
        #region Declares

        private readonly Scene m_scene;
        // saved last reported value so there is something available for llGetRegionFPS 
        private volatile float lastReportedSimFPS = 0;
        private volatile float simFPS;

        public float LastReportedSimFPS { get { return lastReportedSimFPS; } set { lastReportedSimFPS = value; } }
        public float SimFPS { get { return simFPS; } }

        #endregion

        #region Constructor

        public SimFrameMonitor(Scene scene)
        {
            m_scene = scene;
        }

        #endregion

        #region Implementation of IMonitor

        public double GetValue()
        {
            return LastReportedSimFPS;
        }

        public string GetName()
        {
            return "SimFrameStats";
        }

        public string GetFriendlyValue()
        {
            return GetValue() + " frames/second";
        }

        #endregion

        #region Other Methods

        public void AddFPS(int fps)
        {
            simFPS += fps;
        }

        public void ResetStats()
        {
            simFPS = 0;
        }

        #endregion
    }
}
