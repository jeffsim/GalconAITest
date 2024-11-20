using System.Collections.Generic;
using System.Linq;

public class GOAPPlanner
{
    public Queue<Action2> Plan(HashSet<string> worldState, HashSet<string> goalState, List<Action2> availableActions)
    {
        var openList = new PriorityQueue<Node>();
        var closedList = new HashSet<HashSet<string>>(HashSet<string>.CreateSetComparer());

        var startNode = new Node(null, 0, worldState, null);
        openList.Enqueue(startNode, 0);

        while (openList.Count > 0)
        {
            var currentNode = openList.Dequeue();

            if (GoalAchieved(goalState, currentNode.State))
            {
                return BuildPlan(currentNode);
            }

            closedList.Add(currentNode.State);

            foreach (var action in availableActions)
            {
                if (action.ArePreconditionsMet(currentNode.State))
                {
                    var newState = action.ApplyEffects(currentNode.State);

                    if (closedList.Any(s => s.SetEquals(newState)))
                        continue;

                    var newNode = new Node(currentNode, currentNode.Cost + action.Cost, newState, action);
                    var heuristic = Heuristic(newState, goalState);
                    openList.Enqueue(newNode, newNode.Cost + heuristic);
                }
            }
        }

        // No plan found
        return null;
    }

    private bool GoalAchieved(HashSet<string> goalState, HashSet<string> currentState)
    {
        return goalState.IsSubsetOf(currentState);
    }

    private float Heuristic(HashSet<string> state, HashSet<string> goal)
    {
        return goal.Count - state.Intersect(goal).Count();
    }

    private Queue<Action2> BuildPlan(Node node)
    {
        var plan = new Stack<Action2>();
        while (node != null && node.Action != null)
        {
            plan.Push(node.Action);
            node = node.Parent;
        }
        return new Queue<Action2>(plan);
    }

    private class Node
    {
        public Node Parent { get; }
        public float Cost { get; }
        public HashSet<string> State { get; }
        public Action2 Action { get; }

        public Node(Node parent, float cost, HashSet<string> state, Action2 action)
        {
            Parent = parent;
            Cost = cost;
            State = state;
            Action = action;
        }
    }
}