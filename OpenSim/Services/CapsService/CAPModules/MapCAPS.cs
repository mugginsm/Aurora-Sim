﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using log4net;
using Nini.Config;
using Aurora.Simulation.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.Capabilities;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using OpenMetaverse;
using Aurora.DataManager;
using Aurora.Framework;
using Aurora.Services.DataService;
using OpenMetaverse.StructuredData;

namespace OpenSim.Services.CapsService
{
    public class MapCAPS : ICapsServiceConnector
    {
        private readonly string m_mapLayerPath = "0001";
        private IRegionClientCapsService m_service;
        private IGridService m_gridService;
        private List<MapBlockData> m_mapLayer = new List<MapBlockData>();

        public void RegisterCaps(IRegionClientCapsService service)
        {
            m_service = service;
            m_gridService = service.Registry.RequestModuleInterface<IGridService>();

            RestMethod method = delegate(string request, string path, string param,
                                                                OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                return MapLayerRequest(request, path, param, httpRequest, httpResponse, m_service.AgentID);
            };
            m_service.AddStreamHandler("MapLayer", new RestStreamHandler("POST", m_service.CreateCAPS("MapLayer", m_mapLayerPath),
                                                      method));
        }

        public void EnteringRegion()
        {
        }

        public void DeregisterCaps()
        {
        }

        /// <summary>
        /// Callback for a map layer request
        /// </summary>
        /// <param name="request"></param>
        /// <param name="path"></param>
        /// <param name="param"></param>
        /// <param name="agentID"></param>
        /// <param name="caps"></param>
        /// <returns></returns>
        public string MapLayerRequest(string request, string path, string param,
                                      OSHttpRequest httpRequest, OSHttpResponse httpResponse, UUID agentID)
        {
            int bottom = (m_service.RegionY / Constants.RegionSize) - 100;
            int top = (int)(m_service.RegionY / Constants.RegionSize) + 100;
            int left = (int)(m_service.RegionX / Constants.RegionSize) - 100;
            int right = (int)(m_service.RegionX / Constants.RegionSize) + 100;

            OSDMap map = (OSDMap)OSDParser.DeserializeLLSDXml(request);

            int flags = map["Flags"].AsInteger();

            OSDArray layerData = new OSDArray();
            layerData.Add(GetOSDMapLayerResponse(bottom, left, right, top, new UUID("00000000-0000-1111-9999-000000000006")));
            OSDArray mapBlocksData = new OSDArray();

            List<MapBlockData> mapBlocks = new List<MapBlockData>();
            if (m_mapLayer != null && m_mapLayer.Count != 0)
            {
                mapBlocks = m_mapLayer;
            }
            else
            {
                List<GridRegion> regions = m_gridService.GetRegionRange(UUID.Zero,
                        left * (int)Constants.RegionSize,
                        right * (int)Constants.RegionSize,
                        bottom * (int)Constants.RegionSize,
                        top * (int)Constants.RegionSize);
                foreach (GridRegion r in regions)
                {
                    if (flags == 0) //Map
                        mapBlocks.Add(MapBlockFromGridRegion(r));
                    else
                        mapBlocks.Add(TerrainBlockFromGridRegion(r));
                }
                m_mapLayer = mapBlocks;
            }
            foreach (MapBlockData block in m_mapLayer)
            {
                //Add to the array
                mapBlocksData.Add(block.ToOSD());
            }
            OSDMap response = MapLayerResponce(layerData, mapBlocksData, flags);
            string resp = OSDParser.SerializeLLSDXmlString(response);
            return resp;
        }

        protected MapBlockData MapBlockFromGridRegion(GridRegion r)
        {
            MapBlockData block = new MapBlockData();
            if (r == null)
            {
                block.Access = (byte)SimAccess.Down;
                block.MapImageID = UUID.Zero;
                return block;
            }
            block.Access = r.Access;
            block.MapImageID = r.TerrainImage;
            block.Name = r.RegionName;
            block.X = (ushort)(r.RegionLocX / Constants.RegionSize);
            block.Y = (ushort)(r.RegionLocY / Constants.RegionSize);
            block.SizeX = (ushort)(r.RegionSizeX);
            block.SizeY = (ushort)(r.RegionSizeY);
            return block;
        }

        protected MapBlockData TerrainBlockFromGridRegion(GridRegion r)
        {
            MapBlockData block = new MapBlockData();
            if (r == null)
            {
                block.Access = (byte)SimAccess.Down;
                block.MapImageID = UUID.Zero;
                return block;
            }
            block.Access = r.Access;
            block.MapImageID = r.TerrainMapImage;
            block.Name = r.RegionName;
            block.X = (ushort)(r.RegionLocX / Constants.RegionSize);
            block.Y = (ushort)(r.RegionLocY / Constants.RegionSize);
            return block;
        }

        protected static OSDMap MapLayerResponce(OSDArray layerData, OSDArray mapBlocksData, int flags)
        {
            OSDMap map = new OSDMap();
            OSDMap agentMap = new OSDMap();
            agentMap["Flags"] = flags;
            map["AgentData"] = agentMap;
            map["LayerData"] = layerData;
            map["MapBlocks"] = mapBlocksData;
            return map;
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        protected static OSDMap GetOSDMapLayerResponse(int bottom, int left, int right, int top, UUID imageID)
        {
            OSDMap mapLayer = new OSDMap();
            mapLayer["Bottom"] = bottom;
            mapLayer["Left"] = left;
            mapLayer["Right"] = right;
            mapLayer["Top"] = top;
            mapLayer["ImageID"] = imageID;

            return mapLayer;
        }
    }
}
