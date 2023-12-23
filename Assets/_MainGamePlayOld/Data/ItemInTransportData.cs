using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

public class ItemInTransportData
{
    private ItemDefn _defn;
    public ItemDefn Defn => _defn == null ? _defn = GameDefns.Instance.ItemDefns[DefnId] : _defn;
    public string DefnId;

    public bool CompletedTransport;
    float percentWalkedBetweenNodes;

    public int subState;
    public float TimeToStartTransporting;

    [SerializeReference] public List<NodeData> PathToTravel;
    static public float movementSpeed = 10f;
    [SerializeReference] public NodeData CurrentNode;
    [SerializeReference] public NodeData CurrentDestNode;
    [SerializeReference] public NodeData NextNodeInPath;

    [SerializeReference] public NeedData Need;
    [SerializeReference] public TownData Town;
    public ItemType ItemType;
    public Vector3 StartLoc;

    public ItemInTransportData(NeedData need, TownData town)
    {
        Need = need;
        Town = town;
        DefnId = need.ItemDefnId;
        ItemType = need.ItemType;
    }

    bool splineForward;
    // bool testDoSpline;
    float pathSplineLength;
    Spline pathSpline;

    public void StartTransporting(NodeData sourceNode, List<NodeData> pathToTravel, int groupIndex)
    {
        // subState = 0;
        // sourceNode.AddItem(ItemType, -1);

        // // Create copy in case caller changes list
        // PathToTravel = new List<NodeData>(pathToTravel);

        // CurrentNode = sourceNode;
        // CurrentDestNode = PathToTravel[1];
        // NextNodeInPath = PathToTravel[1];
        // CurrentNode.AddItemToExitQueue(this);
        // WorldLoc = sourceNode.WorldLoc;
        // StartLoc = WorldLoc;
        // TimeToStartTransporting = GameTime.time + groupIndex * .05f;
        // percentWalkedBetweenNodes = 0;

        // getPathSpline();
    }

    // private void getPathSpline()
    // {
    //     Debug.Assert(PathToTravel != null, "Null pathtotravel");
    //     Debug.Assert(PathToTravel.Count > 0 && PathToTravel[0] != null, "Empty pathtotravel");

    //     // Worker is walking from pathToTravel[0] (currentNode) to pathToTravel[1]
    //     NextNodeInPath = PathToTravel[1];
    //     Debug.Assert(NextNodeInPath != null, "invalid NextNodeInPath");

    //     var connection = PathToTravel[0].Town.GetNodeConnection(PathToTravel[0], NextNodeInPath);
    //     Debug.Assert(connection.Spline != null, "null connection pathspline - " + PathToTravel[0].Id + "-" + NextNodeInPath.Id);

    //     splineForward = (connection.Node1Id == PathToTravel[0].Id && connection.IsForwardConnection) ||
    //                     (connection.Node2Id == PathToTravel[0].Id && !connection.IsForwardConnection);
    //     pathSpline = connection.Spline;
    //     pathSplineLength = pathSpline.GetLength();
    // }

    // public void Update()
    // {
    //     switch (subState)
    //     {
    //         case 0:
    //             // Waiting our turn to go
    //             if (CurrentNode.ItemIsReadyToLeaveExitQueue(this))
    //             {
    //                 CurrentNode.RemoveItemFromExitQueue(this);
    //                 subState = 1;
    //             }
    //             break;

    //         case 1:

    //             if (pathSpline == null)
    //                 getPathSpline(); // recompile

    //             percentWalkedBetweenNodes = Math.Min((percentWalkedBetweenNodes + movementSpeed * Time.deltaTime / pathSplineLength), 1);
    //             var percent = splineForward ? percentWalkedBetweenNodes : 1 - percentWalkedBetweenNodes;
    //             var posOnSplineLocal = SplineUtility.EvaluatePosition(pathSpline, percent);
    //             WorldLoc = new Vector3(posOnSplineLocal.x, posOnSplineLocal.y, posOnSplineLocal.z);

    //             if (percentWalkedBetweenNodes < 0.999f)
    //                 return; // still walking

    //             // Reached next node in the path.
    //             PathToTravel.RemoveAt(0);
    //             if (PathToTravel.Count > 1)
    //             {
    //                 // still another node to walk through
    //                 WorldLoc = CurrentDestNode.WorldLoc;
    //                 CurrentNode = CurrentDestNode;
    //                 subState = 1;
    //                 percentWalkedBetweenNodes = 0;
    //                 CurrentDestNode = PathToTravel[1];

    //                 var connection = CurrentNode.Town.GetNodeConnection(CurrentNode, CurrentDestNode);
    //                 splineForward = (connection.Node1Id == PathToTravel[0].Id && connection.IsForwardConnection) ||
    //                                 (connection.Node2Id == PathToTravel[0].Id && !connection.IsForwardConnection);
    //                 pathSpline = connection.Spline;
    //                 pathSplineLength = pathSpline.GetLength();
    //             }
    //             else
    //             {
    //                 // Read end.
    //                 // Special case - add goldcoin to Camp == add to global Gold value; destroy the item
    //                 if (CurrentDestNode.BuildingInNode != null && CurrentDestNode.BuildingInNode.Defn.BuildingClass == BuildingClass.Camp && DefnId == "GoldCoin")
    //                 {
    //                     Town.Gold += 1;
    //                 }
    //                 else
    //                 {
    //                     CurrentDestNode.AddItem(ItemType);
    //                 }
    //                 CompletedTransport = true;
    //                 Town.ItemInTransportReachedDestNode(this);
    //             }
    //             break;
    //     }
    // }
}
