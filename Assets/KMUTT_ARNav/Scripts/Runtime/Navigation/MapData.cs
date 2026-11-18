using System;
using System.Collections.Generic;
using UnityEngine;

namespace KMUTT.ARNav
{
    /// <summary>
    /// JSON schema for Data/kmutt_cpe_floors.json (generated from kmutt_cpe_floors_10_11.obj).
    /// Coordinates are already converted to Unity space: x = OBJ X, y = OBJ Z (up), z = OBJ Y. Meters.
    /// </summary>
    [Serializable]
    public class MapBox
    {
        public string name;
        public Vector3 center;
        public Vector3 size;
        public int sourceIndex;
    }

    [Serializable]
    public class MapFloorData
    {
        public string name;     // "Floor_10"
        public int level;       // 10
        public float baseY;     // floor slab height in map space (m)
        public float height;    // storey wall height (m)
        public List<MapBox> walls = new List<MapBox>();
        public List<MapBox> zones = new List<MapBox>();   // room/corridor volumes (non-solid)
    }

    /// <summary>Pre-computed navigation node (traced from the floor plans). position is in map space.</summary>
    [Serializable]
    public class MapNode
    {
        public string nodeId;
        public string displayName;
        public string type;          // NavNodeType name: Waypoint, Room, Stairs, Elevator…
        public int floor;
        public Vector3 position;
        public bool isDestination;
        public string transitionGroup;
        public bool auto;            // true = corridor/room waypoint, false = room / stairs / lift
    }

    [Serializable]
    public class MapLink { public int a; public int b; }

    [Serializable]
    public class MapData
    {
        public string source;
        public string units;
        public float metersPerPixel;
        public List<MapFloorData> floors = new List<MapFloorData>();
        public List<MapNode> nodes = new List<MapNode>();
        public List<MapLink> links = new List<MapLink>();

        public static MapData FromJson(string json) => JsonUtility.FromJson<MapData>(json);
    }
}
