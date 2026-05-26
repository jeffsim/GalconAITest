using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Active gatherer-worker trips: dispatch from gatherer buildings, travel to adjacent
/// deposits, gather, return with cargo, deposit to inventory, rest, repeat.
/// </summary>
public static class ResourceGathering
{
    public static bool TickGathererBuildings(TownData town, float deltaSeconds)
    {
        if (town == null || deltaSeconds <= 0f) return false;
        bool changed = false;

        for (int i = 0; i < town.Nodes.Count; i++)
        {
            var node = town.Nodes[i];
            if (node.OwnedBy == null) continue;
            var building = node.Building;
            if (building == null) continue;
            var defn = building.Defn;
            if (!defn.CanGatherResources || defn.ResourceThisNodeCanGoGather == null) continue;

            if (town.WorldTime < building.NextGathererDispatchTime) continue;
            if (node.NumWorkers <= 0) continue;

            var deposit = FindAdjacentDeposit(node, defn.ResourceThisNodeCanGoGather.GoodType);
            if (deposit == null) continue;

            DispatchGatherer(town, node, deposit, defn, building);
            changed = true;

            float interval = defn.SecondsBetweenGathererDispatch;
            float variance = Random.Range(defn.GathererDispatchIntervalVarianceMin, defn.GathererDispatchIntervalVarianceMax);
            building.NextGathererDispatchTime = town.WorldTime + Mathf.Max(0.05f, interval + variance);
        }

        return changed;
    }

    public static bool AdvanceAllGatheringWorkers(TownData town, float deltaSeconds, float gameSpeed)
    {
        if (town == null || deltaSeconds <= 0f) return false;
        bool changed = false;

        for (int i = 0; i < town.Nodes.Count; i++)
        {
            var home = town.Nodes[i];
            if (home.GatheringWorkers.Count == 0) continue;

            for (int w = home.GatheringWorkers.Count - 1; w >= 0; w--)
            {
                var gw = home.GatheringWorkers[w];
                if (AdvanceOne(town, gw, deltaSeconds, gameSpeed))
                    changed = true;
            }
        }

        return changed;
    }

    public static NodeData FindAdjacentDeposit(NodeData gathererNode, GoodType goodType)
    {
        if (gathererNode == null || goodType == GoodType.Unset) return null;
        var conns = gathererNode.NodeConnections;
        for (int i = 0; i < conns.Count; i++)
        {
            var nb = conns[i].End;
            if (nb?.Building == null) continue;
            var bd = nb.Building.Defn;
            if (!bd.CanBeGatheredFrom) continue;
            if (bd.ResourceGatheredFromThisNode == null) continue;
            if (bd.ResourceGatheredFromThisNode.GoodType == goodType)
                return nb;
        }
        return null;
    }

    static void DispatchGatherer(TownData town, NodeData home, NodeData deposit, BuildingDefn defn, BuildingData building)
    {
        home.NumWorkers--;
        var gw = new GatheringWorkerData(home.OwnedBy, home, deposit, defn);
        home.GatheringWorkers.Add(gw);

        if (building.NextGathererDispatchTime <= 0f)
            building.NextGathererDispatchTime = town.WorldTime;
    }

    static bool AdvanceOne(TownData town, GatheringWorkerData gw, float deltaSeconds, float gameSpeed)
    {
        var home = gw.HomeNode;
        var deposit = gw.DepositNode;

        switch (gw.Phase)
        {
            case GatheringWorkerPhase.GoingToDeposit:
                if (!IsDepositStillValid(deposit, gw.GathererBuildingDefn))
                {
                    BeginReturnEmpty(gw);
                    return true;
                }
                gw.AdvanceAlongSegment(home, deposit, deltaSeconds, gameSpeed);
                if (!gw.ReachedSegmentEnd) return false;
                return ResolveArrivalAtDeposit(town, gw);

            case GatheringWorkerPhase.GatheringAtDeposit:
                if (!IsDepositStillValid(deposit, gw.GathererBuildingDefn))
                {
                    BeginReturnEmpty(gw);
                    return true;
                }
                gw.PhaseTimer -= deltaSeconds;
                if (gw.PhaseTimer > 0f) return false;
                gw.CarriedGood = gw.GathererBuildingDefn.ResourceThisNodeCanGoGather.GoodType;
                BeginReturnWithCargo(gw);
                return true;

            case GatheringWorkerPhase.ReturningHome:
                gw.AdvanceAlongSegment(deposit, home, deltaSeconds, gameSpeed);
                if (!gw.ReachedSegmentEnd) return false;
                return ResolveArrivalAtHome(town, gw);

            case GatheringWorkerPhase.RestingAtHome:
                gw.PhaseTimer -= deltaSeconds;
                if (gw.PhaseTimer > 0f) return false;
                home.GatheringWorkers.Remove(gw);
                home.NumWorkers++;
                return true;

            default:
                return false;
        }
    }

    static bool IsDepositStillValid(NodeData deposit, BuildingDefn gathererDefn)
    {
        if (deposit?.Building == null || gathererDefn?.ResourceThisNodeCanGoGather == null) return false;
        var bd = deposit.Building.Defn;
        return bd.CanBeGatheredFrom
            && bd.ResourceGatheredFromThisNode != null
            && bd.ResourceGatheredFromThisNode.GoodType == gathererDefn.ResourceThisNodeCanGoGather.GoodType;
    }

    static bool ResolveArrivalAtDeposit(TownData town, GatheringWorkerData gw)
    {
        var deposit = gw.DepositNode;

        // Neutral garrison on the deposit: 1:1 trade, gatherer may die.
        if (deposit.OwnedBy == null && deposit.NumWorkers > 0)
        {
            deposit.NumWorkers--;
            RemoveGatherer(gw);
            return true;
        }

        // Hostile deposit (shouldn't happen for static Forest/StoneMine, but handle cleanly).
        if (deposit.OwnedBy != null && deposit.OwnedBy != gw.OwnedBy)
        {
            deposit.AttackHeat += TownData.AttackHeatPerHostileArrival;
            if (deposit.NumWorkers > 0)
            {
                deposit.NumWorkers--;
                RemoveGatherer(gw);
                return true;
            }
        }

        gw.Phase = GatheringWorkerPhase.GatheringAtDeposit;
        gw.PhaseTimer = gw.GathererBuildingDefn.SecondsToGatherAtResource;
        gw.SegmentProgress = 0f;
        gw.WorldLoc = deposit.WorldLoc;
        return true;
    }

    static void BeginReturnWithCargo(GatheringWorkerData gw)
    {
        gw.Phase = GatheringWorkerPhase.ReturningHome;
        gw.SegmentProgress = 0f;
        gw.WorldLoc = gw.DepositNode.WorldLoc;
    }

    static void BeginReturnEmpty(GatheringWorkerData gw)
    {
        gw.CarriedGood = GoodType.Unset;
        gw.Phase = GatheringWorkerPhase.ReturningHome;
        gw.SegmentProgress = 0f;
        gw.WorldLoc = gw.HomeNode.WorldLoc;
    }

    static bool ResolveArrivalAtHome(TownData town, GatheringWorkerData gw)
    {
        var home = gw.HomeNode;
        var player = gw.OwnedBy;
        var carried = gw.CarriedGood;
        if (carried == GoodType.Unset)
            carried = ResourceProduction.GetProducedGoodType(gw.GathererBuildingDefn);
        gw.CarriedGood = GoodType.Unset;

        // Home still ours with a gatherer building: deposit and rest.
        if (home.OwnedBy == player
            && home.Building != null
            && home.Building.Defn.CanGatherResources
            && home.Building.Defn.ResourceThisNodeCanGoGather != null)
        {
            if (carried != GoodType.Unset)
            {
                ResourceProduction.CreditInventory(home.Inventory, carried, 1);
                town.WorldRevision++;
            }
            BeginRest(gw, home.Building.Defn);
            return true;
        }

        // Friendly node, different (or no) building: join the garrison.
        if (home.OwnedBy == player)
        {
            if (carried != GoodType.Unset)
            {
                ResourceProduction.CreditInventory(home.Inventory, carried, 1);
                town.WorldRevision++;
            }
            RemoveGatherer(gw);
            home.NumWorkers++;
            return true;
        }

        // Enemy home: attack using standard combat resolution.
        if (home.OwnedBy != null && home.OwnedBy != player)
        {
            RemoveGatherer(gw);
            ResolveGathererAttackAtNode(town, player, home);
            return true;
        }

        // Neutral / abandoned home: worker is lost.
        RemoveGatherer(gw);
        return true;
    }

    static void BeginRest(GatheringWorkerData gw, BuildingDefn defn)
    {
        gw.Phase = GatheringWorkerPhase.RestingAtHome;
        gw.PhaseTimer = defn.SecondsRestBetweenGatherRuns;
        gw.SegmentProgress = 0f;
        gw.WorldLoc = gw.HomeNode.WorldLoc;
    }

    static void ResolveGathererAttackAtNode(TownData town, PlayerData attacker, NodeData dest)
    {
        dest.AttackHeat += TownData.AttackHeatPerHostileArrival;
        if (dest.NumWorkers > 0)
        {
            dest.NumWorkers--;
            return;
        }

        if (dest.Building != null)
        {
            dest.OwnedBy = attacker;
            dest.NumWorkers = 1;
        }
    }

    static void RemoveGatherer(GatheringWorkerData gw)
    {
        gw.HomeNode?.GatheringWorkers.Remove(gw);
    }

    /// <summary>When a gatherer building is first constructed, allow immediate dispatch.</summary>
    public static void OnGathererBuildingConstructed(NodeData node)
    {
        if (node?.Building == null) return;
        if (!node.Building.Defn.CanGatherResources) return;
        node.Building.NextGathererDispatchTime = 0f;
    }
}
