using System;
using System.Collections.Generic;
using UnityEngine;

public class Algorithm_ABNegaMax
{
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
        return abNegamax(board, 0, int.MinValue, int.MaxValue, out int score);
    }

    // ref: https://csharp.hotexamples.com/examples/-/IBoardGame/-/php-iboardgame-class-examples.html
    // ref: https://github.com/laurentiu-ilici/Checkers-and-Reversi/blob/master/GameAI/Algorithms.cs
    private AIMove abNegamax(AIGameData board, int currentDepth, int alpha, int beta, out int bestScore)
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
            return null;
        }

        var moves = board.getMoves();

        AIMove bestMove = moves[0];
        foreach (var move in moves)
        {
            PlayerAIData.TotalMoves++;
            move.Score = int.MinValue;

            // Create a new copy of the game's data and apply the move to it
            var newBoard = AIGameData.Get(board);
            move.Apply(newBoard);
            newBoard.Tick(); // e.g. generate items

            // Recurse down from the opposing player's perspective
            newBoard.ChangePlayer();

            abNegamax(newBoard, currentDepth + 1, -beta, -Math.Max(alpha, bestMove.Score), out move.Score);
            move.Score *= -1;

            // if (move.AIAction == AIAction.None) // downplay waiting in winning moves - prefer  action
            //     move.Score /= 2;

            // Determine if the current move is the best move
            if (move.Score > bestMove.Score)
                bestMove = move;

            if (bestMove.Score > beta)
                break;

            newBoard.ReturnToPool();
        }

        foreach (var move in moves)
            if (move != bestMove)
                move.ReturnToPool();

        bestScore = bestMove.Score;
        return bestMove;
    }

    // public AIMove GetAIMove(AIGameData board, int depth, int playerId)
    // {
    //     // The "convienence" function that allows us to use our AI algorithm.
    //     List<AIMove> validMoves = board.getMoves();
    //     validMoves = validMoves.OrderBy(a => rng.Next(-10, 10)).ToList();
    //     if (validMoves.Count > 0)
    //     {
    //         int bestScore;
    //         if (playerId == board.Players[0].Id)
    //         {
    //             bestScore = int.MinValue;
    //         }
    //         else if (playerId == Player2Id)
    //         {
    //             bestScore = int.MaxValue;
    //         }
    //         else
    //         {
    //             return null;
    //         }

    //         AIMove bestMove = validMoves[0];
    //         // board.evaluate(playerId) + board.evaluate(board.OtherPlayer(playerId)) + 
    //         // if (GetScore(board, Black) + GetScore(board, White) > 55)
    //         //   depth = 100;

    //         foreach (AIMove move in validMoves)
    //         {
    //             AIGameData childBoard = board.Copy();
    //             board.makeMove(move, playerId);
    //             int nodeScore;
    //             if (playerId == Player1Id)
    //             {
    //                 nodeScore = MinimaxAlphaBeta(childBoard, depth - 1, int.MinValue, int.MaxValue, board.OtherPlayer(playerId), false);
    //                 if (nodeScore > bestScore)
    //                 {
    //                     bestScore = nodeScore;
    //                     bestMove = move;
    //                 }
    //             }
    //             else
    //             {
    //                 nodeScore = MinimaxAlphaBeta(childBoard, depth - 1, int.MinValue, int.MaxValue, board.OtherPlayer(playerId), true);
    //                 if (nodeScore < bestScore)
    //                 {
    //                     bestScore = nodeScore;
    //                     bestMove = move;
    //                 }
    //             }
    //         }
    //         return bestMove;
    //     }
    //     return null;
    // }
    // Ref: https://github.com/oliverzh2000/reversi/blob/master/src/Game.cs
    // public static int MinimaxAlphaBeta(AIGameData board, int depth, int a, int b, int playerId, bool isMaxPlayer)
    // {
    //     // The heart of our AI. Minimax algorithm with alpha-beta pruning to speed up computation.
    //     // Higher search depths = greater difficulty.
    //     if (depth == 0 || board.isGameOver())
    //     {
    //         return board.evaluate(playerId);
    //     }

    //     int bestScore;
    //     if (isMaxPlayer) bestScore = int.MinValue;
    //     else bestScore = int.MaxValue;
    //     List<AIMove> validMoves = board.getMoves(playerId);
    //     if (validMoves.Count > 0)
    //     {
    //         foreach (AIMove move in validMoves)
    //         {
    //             AIGameData childBoard = board.Copy();
    //             childBoard.makeMove(move, playerId);
    //             int nodeScore = MinimaxAlphaBeta(childBoard, depth - 1, a, b, board.OtherPlayer(playerId), !isMaxPlayer);
    //             if (isMaxPlayer)
    //             {
    //                 bestScore = Math.Max(bestScore, nodeScore);
    //                 a = Math.Max(bestScore, a);
    //             }
    //             else
    //             {
    //                 bestScore = Math.Min(bestScore, nodeScore);
    //                 b = Math.Min(bestScore, b);
    //             }
    //             if (b <= a)
    //             {
    //                 break;
    //             }
    //         }
    //     }
    //     else
    //     {
    //         return MinimaxAlphaBeta(board, depth, a, b, board.OtherPlayer(playerId), !isMaxPlayer);
    //     }
    //     return bestScore;
    // }

    // AIMove minimax(AIGameData board, int playerId, int currentDepth, out int bestScore)
    // {
    //     Debug.Log(currentDepth);
    //     if (board.isGameOver() || currentDepth == maxDepth)
    //     {
    //         bestScore = board.evaluate(playerId);
    //         return null;
    //     }

    //     AIMove bestMove = null;
    //     bestScore = -int.MaxValue;

    //     var moves = board.getMoves();
    //     foreach (var move in moves)
    //     {
    //         var newBoard = board.makeMove(move);
    //         var currentMove = minimax(newBoard, currentDepth + 1, out int recursedScore);
    //         var currentScore = -recursedScore;
    //         if (currentScore > bestScore)
    //         {
    //             bestScore = currentScore;
    //             bestMove = move;
    //         }
    //     }
    //     return bestMove;
    // }
    // AIMove negamax(AIGameData board, int currentDepth, out int bestScore)
    // {
    //     Debug.Log(currentDepth);
    //     if (board.isGameOver() || currentDepth == maxDepth)
    //     {
    //         bestScore = board.evaluate();
    //         return null;
    //     }

    //     AIMove bestMove = null;
    //     bestScore = -int.MaxValue;

    //     var moves = board.getMoves();
    //     foreach (var move in moves)
    //     {
    //         var newBoard = board.makeMove(move);
    //         var currentMove = negamax(newBoard, currentDepth + 1, out int recursedScore);
    //         var currentScore = -recursedScore;
    //         if (currentScore > bestScore)
    //         {
    //             bestScore = currentScore;
    //             bestMove = move;
    //         }
    //     }
    //     return bestMove;
    // }

    // public int AlphaBeta(AIGameData board, int depth, AIGameData_Player playerFirst, AIGameData_Player playerSecond, int alpha, int beta)
    // {
    //     if (board.isGameOver() == true || depth == simulationDepth)
    //     {
    //         return board.evaluate(playerFirst) - board.evaluate(playerSecond);
    //     }

    //     if (depth % 2 == 0)
    //     {
    //         int bestScore = int.MinValue;
    //         List<AIMove> moves = board.getMoves(playerFirst);

    //         foreach (AIMove move in moves)
    //         {
    //             AIGameData afterMove = board.Copy();
    //             afterMove.makeMove(move);
    //             int score = AlphaBeta(afterMove, depth + 1, playerFirst, playerSecond, alpha, beta);
    //             bestScore = Mathf.Max(bestScore, score);
    //             alpha = Mathf.Max(alpha, bestScore);
    //             if (alpha >= beta)
    //                 break;
    //         }
    //         return bestScore;
    //     }
    //     else
    //     {
    //         int bestScore = int.MaxValue;
    //         List<AIMove> moves = board.getMoves(playerSecond);

    //         foreach (AIMove move in moves)
    //         {
    //             AIGameData afterMove = board.Copy();
    //             afterMove.makeMove(move);
    //             int score = AlphaBeta(afterMove, depth + 1, playerFirst, playerSecond, alpha, beta);
    //             bestScore = Mathf.Min(bestScore, score);
    //             beta = Mathf.Min(beta, bestScore);
    //             if (beta <= alpha)
    //                 break;
    //         }
    //         return bestScore;
    //     }
    // }
}
