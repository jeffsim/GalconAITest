using UnityEngine;

public partial class Strategy_NonRecursive
{
    private void CheckPriority_AttackEnemyNodes()
    {
        /*
            find all enemy nodes that we can reach; i.e. they are adjacent to on of PlayerNodes
            foreach enemy node, determine (A) if we have enough workers to do so and (B) the value of doing so
            to calculate (A), we do a simple BFS outward ONLY through neighboring nodes that we own
                 if one of the nodes we own has workers to spare (and doesn't itself want to keep them) then they're added to the sum of workers
                 AI can stop at 3 BFS or go deeper.  when we have enoguh workers, we can stop the BFS
            to calculate (B), we need to know
                how much the AI personality values attacking enemy nodes
                how much the AI personality values owning nodes

        */
        int numEnemyNodes = EnemyNodes.Count;
        float rawValue = 0f;
        for (int i = 0; i < numEnemyNodes; i++)
        {
            var enemyNode = EnemyNodes[i];

            // do we have any nodes next to this enemy node?
            var neighbors = enemyNode.NeighborNodes;
            int numNeighbors = neighbors.Count;
            for (int j = 0; j < numNeighbors; j++)
            {
                var neighbor = neighbors[j];
                if (neighbor.OwnedBy == Player)
                {
                    // we have a neighbor node that we own
                    // do a BFS to see if we have enough workers to attack this node
                
                }
            }
        }
    }
}