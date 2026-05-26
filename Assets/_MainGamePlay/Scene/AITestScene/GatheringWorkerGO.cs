using UnityEngine;

/// <summary>
/// Visual for an active gatherer trip. Synced from AITestScene each frame (no Update loop).
/// </summary>
public class GatheringWorker : MonoBehaviour
{
    public GatheringWorkerData Data;
    [System.NonSerialized] public MeshRenderer BodyRenderer;
    [System.NonSerialized] public MeshRenderer CargoRenderer;

    public void InitializeForData(GatheringWorkerData data)
    {
        Data = data;
        name = $"Gatherer [{data.OwnedBy?.Name}] {data.HomeNode?.NodeId}<->{data.DepositNode?.NodeId}";

        BodyRenderer = GetComponent<MeshRenderer>();
        if (BodyRenderer != null)
            BodyRenderer.material.color = GatheringWorkerData.WorkerDisplayColor;

        // Worker prefab is 0.2 scale; combat workers leave it alone. Gatherers are half that.
        transform.localScale *= GatheringWorkerData.VisualScaleRelativeToWorkerPrefab;

        EnsureCargoCube();
        RefreshCargoVisual();
        transform.position = data.WorldLoc + new Vector3(0, .2f, 0);
    }

    void EnsureCargoCube()
    {
        if (CargoRenderer != null) return;
        var cargo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cargo.name = "Cargo";
        cargo.transform.SetParent(transform, false);
        cargo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        cargo.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        var col = cargo.GetComponent<Collider>();
        if (col != null) Destroy(col);
        CargoRenderer = cargo.GetComponent<MeshRenderer>();
    }

    public void SyncFromData()
    {
        if (Data == null) return;
        transform.position = Data.WorldLoc + new Vector3(0, .2f, 0);
        RefreshCargoVisual();
    }

    void RefreshCargoVisual()
    {
        EnsureCargoCube();
        if (CargoRenderer == null) return;

        bool show = Data.Phase == GatheringWorkerPhase.ReturningHome && Data.IsCarrying;
        CargoRenderer.enabled = show;
        if (!show) return;

        Color c = Color.white;
        if (GameDefns.Instance != null)
        {
            foreach (var gd in GameDefns.Instance.GoodDefns.Values)
            {
                if (gd.GoodType == Data.CarriedGood)
                {
                    c = gd.GoodColor;
                    break;
                }
            }
        }
        CargoRenderer.material.color = c;
    }
}
