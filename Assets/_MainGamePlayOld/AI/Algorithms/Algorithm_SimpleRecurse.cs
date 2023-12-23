using System.Collections.Generic;
using UnityEngine;

public class Algorithm_SimpleRecurse
{
    // This evaluator ignores the opposing player and just says "what if I did X, then Y, then Z?" and finds the XYZ that results in the highest score
    private int simulationDepth = 2;

    HashSet<string> visited = new HashSet<string>();
    Dictionary<string, int> visited2 = new Dictionary<string, int>();
    public int NumRevisits = 0;
    public int NumRevisits2 = 0;
    public AIMove GetBestMove(AIGameData board, EnemyIntelligence intel)
    {
        visited.Clear();
        visited2.Clear();
        NumRevisits = 0;
        NumRevisits2 = 0;
        simulationDepth = intel == EnemyIntelligence.Slow ? 1 : 2;

        return simpleRecurse(board, 0, out int score);
    }

    private AIMove simpleRecurse(AIGameData board, int currentDepth, out int bestScore)
    {
        // If too deep then abort.  NOTE: This is NOT a proper iterative depth search.  It's just to avoid locking up Unity
        if (PlayerAIData.TotalMoves > 10000) 
        {
            bestScore = board.evaluate();
            Debug.LogError("Too deep search; aborting");
            return null;
        }

        if ((!Settings.ExhaustAISearchTree && board.isGameOver() || currentDepth == simulationDepth))
        {
            bestScore = board.evaluate();

            // var hash = board.GetHash();
            // if (visited2.ContainsKey(hash))
            // NumRevisits2++;
            // if (visited2.ContainsKey(hash))
            //     Debug.Assert(visited2[hash] == bestScore);
            // visited2[hash] = bestScore;

            return null;
        }

        var moves = board.getMoves();

        AIMove bestMove = moves[0];
        foreach (var move in moves)
        {
            PlayerAIData.TotalMoves++;

            // Create a new copy of the game's data and apply the move to it
            var newBoard = AIGameData.Get(board);
            move.Apply(newBoard);
            newBoard.Tick(); // e.g. generate items

            // var hash = board.GetHash();
            // if (visited.Contains(hash))
            //     NumRevisits++;
            // else
            //     visited.Add(hash);
            // if (visited2.ContainsKey(hash))
            //     NumRevisits2++;

            simpleRecurse(newBoard, currentDepth + 1, out move.Score);

            // if (move.AIAction == AIAction.None && move.Score > 0) // downplay waiting in winning moves - prefer  action
            //     move.Score /= 2;

            // Determine if the current move is the best move
            if (move.Score >= bestMove.Score)
                bestMove = move;

            newBoard.ReturnToPool();
        }

        foreach (var move in moves)
            if (move != bestMove)
                move.ReturnToPool();

        bestScore = bestMove.Score;
        return bestMove;
    }

    // newBoard.SetAction_Debug_RemoveThis_Applied(move);
    // if (true
    //     && newBoard.CurrentPlayerId == 2
    //     && newBoard.Debug_FirstMoveTurnsAgo(2)
    //     && newBoard.Debug_MoveIs(1, AIAction.ConstructBuilding, 5, null) // construct any building in node 5
    //     && newBoard.Debug_MoveIs(0, AIAction.None)                       // wait
    //     )
    // {
    //     board = board;
    // }
    // if (false
    //     || newBoard.Debug_MoveIs(0, AIAction.ConstructBuilding, 2, null) 
    //     || newBoard.Debug_MoveIs(1, AIAction.ConstructBuilding, 2, null) 
    //     || newBoard.Debug_MoveIs(2, AIAction.ConstructBuilding, 2, null) 
    //     || newBoard.Debug_MoveIs(3, AIAction.ConstructBuilding, 2, null) 
    //     || newBoard.Debug_MoveIs(4, AIAction.ConstructBuilding, 2, null) 
    //     || newBoard.Debug_MoveIs(5, AIAction.ConstructBuilding, 2, null) 
    //     )
    // {
    //     Debug.Log(2);
    // }
}
