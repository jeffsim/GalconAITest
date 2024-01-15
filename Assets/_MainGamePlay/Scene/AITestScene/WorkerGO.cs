using UnityEngine;

public class Worker : MonoBehaviour
{
    public WorkerData Data;

    public void InitializeForData(WorkerData data)
    {
        name = "Worker - " + data.WorldLoc;
      
        Data = data;
        transform.position = data.WorldLoc + new Vector3(0, .2f, 0);
        GetComponent<MeshRenderer>().material.color = data.OwnedBy.Color;
    }

    void Update()
    {
    }
}
