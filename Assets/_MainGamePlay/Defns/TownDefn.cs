using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Splines;

[Serializable]
public class VisibleSpot
{
    public Vector3 Location;
    public float Radius;
}

public enum WinLoseCondition { None, KillEnemies };

public enum EnemyIntelligence { Slow, Normal };
public enum AIConstraintType
{
    NoBuildingUpgrades, // AI isn't allowed to upgrade buildings.  Only used for tutorial levels?
    DisallowAttackingNode,  // AI isn't allowed to attack a specific node.  Accompanied by the Node Id.
    DontAttackFromNode, // AI isn't allowed to send out attacks from a specific Node.  Accompanied by the Node Id.
}

[Serializable]
public class AIConstraint
{
    public AIConstraintType ConstraintType;

    // [ShowIf("ConstraintType", AIConstraintType.DisallowAttackingNode)]
    public int NodeId;
}

public enum TownEventTrigger
{
    LevelStart, // event is triggered at level start
    NodeCaptured, // event is triggered when node NodeId is captured
    BuildingConstructionCompleted,
    WorkersSentToNode,
    BuildingUpgraded,
}

public enum TownEventType
{
    DisplayText, // displays text when triggered
    LoseLevel, // lose level when triggered
    WinLevel, // win level when triggered
    // UnlockNode, // unlocks TargetNodeId when triggered
}

[Serializable]
public class TownEventDefn
{
    public TownEventTrigger Trigger;

    [ShowInInspector] public Guid Guid = Guid.NewGuid();
    [ShowInInspector] public Guid MustCompleteEventFirst;

    // [ShowIf("Trigger", TownEventTrigger.NodeCaptured)]
    public int NodeId;

    // Things to do when event is triggered
    public TownEventType EventType;

    // Text events
    [ShowIf("EventType", TownEventType.DisplayText)]
    public string Text; // Text to display
    [ShowIf("EventType", TownEventType.DisplayText)]
    public bool TextIsModal;
    [ShowIf("EventType", TownEventType.DisplayText)]
    public int TextFadeOutTime; // Fades out nonmodal text after this many seconds

    [ShowIf("EventType", TownEventType.DisplayText)]
    public int TextPointsAtNodeId = -1; // if -1 then points at none

    // [ShowIf("EventType", TownEventType.UnlockNode)]
    // public int TargetNodeId; // Node to apply triggered event to
}

[CreateAssetMenu(fileName = "TownDefn")]
public class TownDefn : BaseDefn
{
    public string FriendlyName;

    public string TutorialStageId;
    
    public GameObject Ground;
    public Vector3 GroundOffset;

    public WinLoseCondition WinLoseCondition = WinLoseCondition.KillEnemies;

    public List<TownEventDefn> Events;

    // Position in the WorldMap
    public Vector3 WorldLoc;

    public Vector3 StartingCameraPosition = new Vector3(44, 44, -11);

    public List<RaceDefn> PlayerRaces = new List<RaceDefn>();

    public List<VisibleSpot> VisibleSpotsAtStart = new List<VisibleSpot>();

    // AI related
    public EnemyIntelligence EnemyIntelligence = EnemyIntelligence.Normal;
    public List<AIConstraint> AIConstraints = new List<AIConstraint>();

    public List<Town_NodeDefn> Nodes = new List<Town_NodeDefn>();

    public List<Town_NodeConnectionDefn> NodeConnections = new List<Town_NodeConnectionDefn>();
}
