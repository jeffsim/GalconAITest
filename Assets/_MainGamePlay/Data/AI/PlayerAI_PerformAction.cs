using System;
using UnityEngine;

public partial class PlayerAI
{
    private void performAction(AIAction bestAction)
    {
        if (!GameMgr.Instance.DebugOutputStrategy)
            return;
        if (bestAction.Type == AIActionType.DoNothing)
            return; // no action to take

        Debug.Log("----------------------");
        var actionToOutput = bestAction;
        // int spaces = 0;
        while (actionToOutput.DebugOutput_NextAction != null)
        {
            // create empty string with 'spaces' indentation
            string str = "";
            // str += new string(' ', Math.Max(0, spaces - 1) * 4);
            // if (actionToOutput != bestAction)
            //     str += "\u21B3";

            str += "Depth: " + actionToOutput.DebugOutput_Depth;
            str += " | Recursion: " + actionToOutput.DebugOutput_RecursionNum;
            str += " | Action: " + actionToOutput.DebugOutput_TriedActionNum;

            str += " | Score: " + actionToOutput.ScoreBeforeSubActions.ToString("0.0") + "=>" + actionToOutput.Score.ToString("0.0");
            str += " | Action: ";
            switch (actionToOutput.Type)
            {
                case AIActionType.SendWorkersToNode:
                    str += "Send " + actionToOutput.Count + " workers from " + actionToOutput.SourceNode.NodeId + " to " + actionToOutput.DestNode.NodeId;
                    break;
                case AIActionType.AttackFromNode:
                    str += "Attack with " + actionToOutput.Count + " workers from " + actionToOutput.SourceNode.NodeId + " to " + actionToOutput.DestNode.NodeId + " and capture it";
                    break;
                case AIActionType.ConstructBuildingInOwnedNode:
                    str += "Construct " + actionToOutput.BuildingToConstruct.Id + " in " + actionToOutput.SourceNode.NodeId;
                    break;
                case AIActionType.DoNothing: str += "Do nothing (No beneficial action found)"; break;
                case AIActionType.NoAction_MaxDepth: str += "Max depth reached"; break;
                case AIActionType.NoAction_GameOver: str += "Game Over"; break;
                default:
                    throw new Exception("Unhandled AIActionType: " + actionToOutput.Type);
            }

            // add score reasons
            if (GameMgr.Instance.DebugOutputStrategyFull)
            {
                str += " | Score reasons: ";
                str += actionToOutput.DebugOutput_ScoreReasonsBeforeSubActions;
            }
            Debug.Log(str);
            // spaces++;
            actionToOutput = actionToOutput.DebugOutput_NextAction;
        }
    }
}
