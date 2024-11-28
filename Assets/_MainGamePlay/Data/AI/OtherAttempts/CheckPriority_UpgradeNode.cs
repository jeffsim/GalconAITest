using UnityEngine;

public partial class Strategy_NonRecursive
{
    private void CheckPriority_UpgradeNode()
    {
        int playerNodesCount = PlayerNodes.Count;
        for (int i = 0; i < playerNodesCount; i++)
        {
            var node = PlayerNodes[i];
            float rawValue = 0f;

            // can't upgrade if < maxworkers
            if (node.NumWorkers < node.MaxWorkers)
                continue;

            // if here then have >= max workers.  At least minimum desire to upgrade (if nothing better to do)
            rawValue = upgradeNodeMinScore * 1.1f;

            // 1. Calculate base value based on excessive workers
            int numExcessiveWorkers = node.NumWorkers - node.MaxWorkers;
            if (node.NumWorkers > node.MaxWorkers * 1.5f)
                rawValue = 100;
            else if (numExcessiveWorkers > 0)
            {
                float percentExcessive = (float)numExcessiveWorkers / node.MaxWorkers;
                rawValue += Mathf.Pow(percentExcessive, 2) * excessWorkersScalingFactor;

                // If no enemies are nearby, further increase the value
                if (node.NumEnemiesInNeighborNodes == 0)
                    rawValue += Mathf.Pow(numExcessiveWorkers, 2) * excessWorkersScalingFactor2;
            }

            // 2. Adjust value based on nearby enemies
            if (node.NumEnemiesInNeighborNodes > 0)
            {
                float delta = node.NumEnemiesInNeighborNodes - node.NumWorkers;
                rawValue -= Mathf.Pow(delta, 2) * nearbyEnemiesScalingFactor;
            }
            if (node.IsOnTerritoryEdge && node.NumWorkers < node.MaxWorkers * 1.5f && node.NumEnemiesInNeighborNodes > 0)
            {
                rawValue += 35; // TODO
            }

            // if num workers is far from max, increase value
            // if (node.NumWorkers < node.MaxWorkers * 0.25f)
            // {
            //     rawValue += 10;
            // }

            // 3. Normalize the raw value
            // Ensure the rawValue is within the action's original score range before normalization
            // Since the base scores for Upgrade Node are 10-20, but rawValue can vary based on game state
            // We'll map rawValue proportionally to the normalized range [0, 0.333]

            // First, clamp the rawValue to the action's score range to prevent overflows
            float clampedRawValue = Mathf.Clamp(rawValue, 10f, 40f);

            // Normalize the clamped raw value
            float normalizedValue = (clampedRawValue - upgradeNodeMinScore) / (upgradeNodeMaxScore - upgradeNodeMinScore);
            // normalizedValue is now between 0.0 and 0.333

            // 4. Apply AI personality multiplier
            float finalValue = normalizedValue * personalityMultiplier_UpgradeNode;

            // 5. Update Best Action if this action is better than the current best action
            //      if (finalValue > BestAction.Score)
            {
                AIDebuggerEntryData debuggerEntry = null;
#if DEBUG
                if (AITestScene.Instance.TrackDebugAIInfo)
                {
                    debuggerEntry = AIDebugger.rootEntry.AddEntry_UpgradeBuilding(node, finalValue, Player.AI.debugOutput_ActionsTried++, 0);
                }
#endif
                if (finalValue > BestAction.Score)
                    BestAction.SetTo_UpgradeBuilding(node, finalValue, debuggerEntry);
            }
        }
    }
}