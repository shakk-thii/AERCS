using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally lays out the stand-in city along the mission corridor.
///
/// Placing several hundred buildings by hand in the editor would be slow
/// and impossible to re-tune. Generating them from a seeded RNG means the
/// layout is reproducible, the density is a single tunable number, and the
/// city always fits the route length rather than the route having to fit
/// a hand-built city.
///
/// Prefabs are injected from the Inspector rather than looked up by name,
/// so this works with any low-poly asset pack, not just one.
/// </summary>
public class CityBuilder : MonoBehaviour
{
    [Header("Prefabs (drag from the asset pack)")]
    public GameObject roadTilePrefab;
    public GameObject[] buildingPrefabs;
    public GameObject[] propPrefabs;
    public GameObject hospitalPrefab;

    [Header("Layout")]
    [Tooltip("Length of one road tile along the route axis.")]
    public float roadTileLength = 10f;
    [Tooltip("Lateral distance from the route centreline to a building face.")]
    public float buildingSetback = 12f;
    [Tooltip("Average gap between buildings along each side.")]
    public float buildingSpacing = 18f;
    [Tooltip("How many rows of buildings deep on each side.")]
    public int blockDepth = 2;
    public float blockDepthSpacing = 22f;
    [Range(0f, 1f)] public float buildingJitter = 0.35f;
    public int layoutSeed = 7;

    private readonly List<GameObject> spawned = new List<GameObject>();

    /// <summary>
    /// Build the city along the +Z axis, from origin to routeLength.
    /// Returns the parent transform holding everything, so the caller can
    /// clear it without touching the rest of the scene.
    /// </summary>
    public Transform Build(float routeLength)
    {
        Clear();

        GameObject root = new GameObject("City");
        root.transform.SetParent(transform, false);
        System.Random rng = new System.Random(layoutSeed);

        BuildRoad(root.transform, routeLength);
        BuildBlocks(root.transform, routeLength, rng);
        PlaceHospital(root.transform);

        return root.transform;
    }

    private void BuildRoad(Transform parent, float routeLength)
    {
        if (roadTilePrefab == null) return;

        int tiles = Mathf.CeilToInt(routeLength / roadTileLength);
        for (int i = 0; i < tiles; i++)
        {
            Vector3 pos = new Vector3(0f, 0.01f, i * roadTileLength);
            GameObject tile = Instantiate(roadTilePrefab, pos,
                                          Quaternion.identity, parent);
            tile.name = $"Road_{i:D3}";
            spawned.Add(tile);
        }
    }

    private void BuildBlocks(Transform parent, float routeLength, System.Random rng)
    {
        if (buildingPrefabs == null || buildingPrefabs.Length == 0) return;

        // Both sides of the road, several rows deep.
        for (int side = -1; side <= 1; side += 2)
        {
            for (int depth = 0; depth < blockDepth; depth++)
            {
                float lateral = side * (buildingSetback + depth * blockDepthSpacing);
                float z = 0f;

                while (z < routeLength)
                {
                    float jitterZ = (float)(rng.NextDouble() - 0.5)
                                    * buildingSpacing * buildingJitter;
                    float jitterX = (float)(rng.NextDouble() - 0.5) * 3f;

                    GameObject prefab =
                        buildingPrefabs[rng.Next(buildingPrefabs.Length)];

                    if (prefab != null)
                    {
                        Vector3 pos = new Vector3(lateral + jitterX, 0f, z + jitterZ);
                        // Face the road: buildings on the -X side turn 90 deg,
                        // the +X side turns -90, so frontages look inward.
                        Quaternion rot = Quaternion.Euler(0f, side > 0 ? -90f : 90f, 0f);

                        GameObject b = Instantiate(prefab, pos, rot, parent);
                        b.name = $"Building_{side}_{depth}_{z:F0}";
                        spawned.Add(b);
                    }

                    z += buildingSpacing;
                }
            }
        }

        ScatterProps(parent, routeLength, rng);
    }

    private void ScatterProps(Transform parent, float routeLength, System.Random rng)
    {
        if (propPrefabs == null || propPrefabs.Length == 0) return;

        float spacing = 14f;
        for (float z = 0f; z < routeLength; z += spacing)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                if (rng.NextDouble() > 0.55) continue;

                GameObject prefab = propPrefabs[rng.Next(propPrefabs.Length)];
                if (prefab == null) continue;

                Vector3 pos = new Vector3(side * (buildingSetback - 5f), 0f, z);
                GameObject p = Instantiate(prefab, pos, Quaternion.identity, parent);
                p.name = $"Prop_{side}_{z:F0}";
                spawned.Add(p);
            }
        }
    }

    private void PlaceHospital(Transform parent)
    {
        if (hospitalPrefab == null) return;

        // Sits just off the road at the dispatch end, behind the start line.
        Vector3 pos = new Vector3(-buildingSetback - 6f, 0f, -14f);
        GameObject h = Instantiate(hospitalPrefab, pos,
                                   Quaternion.Euler(0f, 90f, 0f), parent);
        h.name = "Hospital";
        spawned.Add(h);
    }

    public void Clear()
    {
        foreach (GameObject go in spawned)
        {
            if (go != null) DestroyImmediate(go);
        }
        spawned.Clear();

        Transform existing = transform.Find("City");
        if (existing != null) DestroyImmediate(existing.gameObject);
    }
}