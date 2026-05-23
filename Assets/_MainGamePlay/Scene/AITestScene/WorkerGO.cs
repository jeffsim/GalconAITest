using UnityEngine;

// Realtime in-flight worker GameObject. The scene's SyncInFlightWorkerGOs is the source of
// truth for position and visibility -- it runs every frame and pokes our transform + the
// MeshRenderer enabled flag based on the WorkerData state. We don't drive ourselves from
// Update because the GO must remain ACTIVE for any per-frame work to happen, and we want
// pre-spawn workers to be invisible without disabling the GO.
public class Worker : MonoBehaviour
{
    public WorkerData Data;
    [System.NonSerialized] public MeshRenderer MeshRenderer;

    public void InitializeForData(WorkerData data)
    {
        Data = data;
        name = $"Worker [{data.OwnedBy?.Name}] {data.FromNode?.NodeId}->{data.ToNode?.NodeId}";

        MeshRenderer = GetComponent<MeshRenderer>();
        if (MeshRenderer != null && data.OwnedBy != null)
            MeshRenderer.material.color = data.OwnedBy.Color;

        // Park at the source until the scene tells us otherwise.
        transform.position = data.WorldLoc + new Vector3(0, .2f, 0);
        if (MeshRenderer != null)
            MeshRenderer.enabled = data.HasSpawned;
    }
}
