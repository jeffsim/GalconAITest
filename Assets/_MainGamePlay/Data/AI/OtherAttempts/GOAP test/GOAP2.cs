// using System;
// using System.Collections.Generic;
// using System.Linq;

// #region Enums for Resources, Items, Buildings, and Units

// public enum ResourceType
// {
//     Wood,
//     Stone
// }

// public enum ItemType
// {
//     Planks,
//     StoneBlocks
// }


// public enum UnitType
// {
//     Worker,
//     Warrior
// }

// #endregion

// #region WorldState Class

// public class WorldState
// {
//     public HashSet<string> Conditions { get; private set; }

//     public WorldState()
//     {
//         Conditions = new HashSet<string>();
//     }

//     public void UpdateWorldState(GameData gameData, int playerId)
//     {
//         Conditions.Clear();

//         // Resources
//         foreach (var resource in gameData.PlayerResources[playerId])
//         {
//             if (resource.Value > 0)
//                 Conditions.Add($"HasResource:{resource.Key}");
//         }

//         // Items
//         foreach (var item in gameData.PlayerItems[playerId])
//         {
//             if (item.Value > 0)
//                 Conditions.Add($"HasItem:{item.Key}");
//         }

//         // Buildings
//         foreach (var building in gameData.PlayerBuildings[playerId])
//         {
//             Conditions.Add($"HasBuilding:{building.BuildingType}");
//         }

//         // Units
//         foreach (var unit in gameData.PlayerUnits[playerId])
//         {
//             Conditions.Add($"HasUnit:{unit.UnitType}");
//         }

//         // Under attack
//         if (gameData.IsPlayerUnderAttack(playerId))
//             Conditions.Add("UnderAttack");
//     }
// }

// #endregion

// #region Action Class

// public class Action
// {
//     public string Name { get; set; }
//     public HashSet<string> Preconditions { get; set; }
//     public HashSet<string> Effects { get; set; }
//     public float Cost { get; set; }

//     public Action()
//     {
//         Preconditions = new HashSet<string>();
//         Effects = new HashSet<string>();
//     }

//     public bool ArePreconditionsMet(HashSet<string> state)
//     {
//         return Preconditions.IsSubsetOf(state);
//     }

//     public HashSet<string> ApplyEffects(HashSet<string> state)
//     {
//         var newState = new HashSet<string>(state);
//         foreach (var effect in Effects)
//         {
//             newState.Add(effect);
//         }
//         return newState;
//     }
// }

// #endregion

// #region ActionFactory Class

// public static class ActionFactory
// {
//     public static List<Action> GetAvailableActions(GameData gameData, int playerId)
//     {
//         var actions = new List<Action>();

//         // Gather Resource Actions
//         foreach (ResourceType resource in Enum.GetValues(typeof(ResourceType)))
//         {
//             var gatherAction = new Action
//             {
//                 Name = $"GatherResource:{resource}",
//                 Preconditions = new HashSet<string> { "HasUnit:Worker", $"HasBuilding:{GetGatheringBuilding(resource)}" },
//                 Effects = new HashSet<string> { $"HasResource:{resource}" },
//                 Cost = 1.0f
//             };
//             actions.Add(gatherAction);
//         }

//         // Craft Item Actions
//         foreach (ItemType item in Enum.GetValues(typeof(ItemType)))
//         {
//             var craftAction = new Action
//             {
//                 Name = $"CraftItem:{item}",
//                 Preconditions = new HashSet<string> { "HasUnit:Worker", $"HasBuilding:{GetCraftingBuilding(item)}", $"HasResource:{GetRequiredResource(item)}" },
//                 Effects = new HashSet<string> { $"HasItem:{item}" },
//                 Cost = 2.0f
//             };
//             actions.Add(craftAction);
//         }

//         // Construct Building Actions
//         foreach (BuildingType building in Enum.GetValues(typeof(BuildingType)))
//         {
//             var requiredItems = GetRequiredItemsForBuilding(building);
//             var preconditions = new HashSet<string> { "HasUnit:Worker" };
//             foreach (var item in requiredItems)
//             {
//                 preconditions.Add($"HasItem:{item}");
//             }

//             var constructAction = new Action
//             {
//                 Name = $"ConstructBuilding:{building}",
//                 Preconditions = preconditions,
//                 Effects = new HashSet<string> { $"HasBuilding:{building}" },
//                 Cost = 3.0f
//             };
//             actions.Add(constructAction);
//         }

//         // Train Unit Actions
//         foreach (UnitType unit in Enum.GetValues(typeof(UnitType)))
//         {
//             if (unit == UnitType.Worker) continue; // Assume workers are available
//             var trainAction = new Action
//             {
//                 Name = $"TrainUnit:{unit}",
//                 Preconditions = new HashSet<string> { $"HasBuilding:{GetTrainingBuilding(unit)}", "HasUnit:Worker", $"HasItem:{GetRequiredItemForUnit(unit)}" },
//                 Effects = new HashSet<string> { $"HasUnit:{unit}" },
//                 Cost = 2.5f
//             };
//             actions.Add(trainAction);
//         }

//         // Defend Action
//         var defendAction = new Action
//         {
//             Name = "Defend",
//             Preconditions = new HashSet<string> { "UnderAttack", "HasUnit:Warrior" },
//             Effects = new HashSet<string> { "Safe" },
//             Cost = 3.0f
//         };
//         actions.Add(defendAction);

//         return actions;
//     }

//     public static ResourceType GetRequiredResource(ItemType item)
//     {
//         return item switch
//         {
//             ItemType.Planks => ResourceType.Wood,
//             ItemType.StoneBlocks => ResourceType.Stone,
//             _ => throw new ArgumentException("Invalid item type"),
//         };
//     }

//     public static List<ItemType> GetRequiredItemsForBuilding(BuildingType building)
//     {
//         return building switch
//         {
//             BuildingType.LumberjackHut => new List<ItemType> { ItemType.Planks },
//             BuildingType.Sawmill => new List<ItemType> { ItemType.Planks, ItemType.StoneBlocks },
//             BuildingType.StoneQuarry => new List<ItemType> { ItemType.Planks },
//             BuildingType.StoneMason => new List<ItemType> { ItemType.Planks, ItemType.StoneBlocks },
//             BuildingType.Barracks => new List<ItemType> { ItemType.Planks, ItemType.StoneBlocks },
//             _ => throw new ArgumentException("Invalid building type"),
//         };
//     }

//     public static BuildingType GetGatheringBuilding(ResourceType resource)
//     {
//         return resource switch
//         {
//             ResourceType.Wood => BuildingType.LumberjackHut,
//             ResourceType.Stone => BuildingType.StoneQuarry,
//             _ => throw new ArgumentException("Invalid resource type"),
//         };
//     }

//     public static BuildingType GetCraftingBuilding(ItemType item)
//     {
//         return item switch
//         {
//             ItemType.Planks => BuildingType.Sawmill,
//             ItemType.StoneBlocks => BuildingType.StoneMason,
//             _ => throw new ArgumentException("Invalid item type"),
//         };
//     }

//     public static BuildingType GetTrainingBuilding(UnitType unit)
//     {
//         return unit switch
//         {
//             UnitType.Warrior => BuildingType.Barracks,
//             _ => throw new ArgumentException("Invalid unit type"),
//         };
//     }

//     public static ItemType GetRequiredItemForUnit(UnitType unit)
//     {
//         return unit switch
//         {
//             UnitType.Warrior => ItemType.Planks, // Assume planks are needed to create weapons
//             _ => throw new ArgumentException("Invalid unit type"),
//         };
//     }
// }

// #endregion

// #region Goal Classes

// public abstract class Goal
// {
//     public string Name { get; set; }
//     public abstract float CalculateUtility(GameData gameData, int playerId);
//     public abstract HashSet<string> GetGoalState();
// }

// public class ConstructBuildingGoal : Goal
// {
//     private BuildingType buildingType;

//     public ConstructBuildingGoal(BuildingType building)
//     {
//         Name = $"ConstructBuilding:{building}";
//         buildingType = building;
//     }

//     public override float CalculateUtility(GameData gameData, int playerId)
//     {
//         // If the player doesn't have this building, high utility
//         bool hasBuilding = gameData.PlayerBuildings[playerId].Any(b => b.BuildingType == buildingType);
//         return hasBuilding ? 0 : 50.0f;
//     }

//     public override HashSet<string> GetGoalState()
//     {
//         return new HashSet<string> { $"HasBuilding:{buildingType}" };
//     }
// }

// public class GatherResourceGoal : Goal
// {
//     private ResourceType resourceType;

//     public GatherResourceGoal(ResourceType resource)
//     {
//         Name = $"GatherResource:{resource}";
//         resourceType = resource;
//     }

//     public override float CalculateUtility(GameData gameData, int playerId)
//     {
//         int currentAmount = gameData.PlayerResources[playerId][resourceType];
//         // The less we have, the higher the utility
//         return 100 - currentAmount;
//     }

//     public override HashSet<string> GetGoalState()
//     {
//         return new HashSet<string> { $"HasResource:{resourceType}" };
//     }
// }

// public class CraftItemGoal : Goal
// {
//     private ItemType itemType;

//     public CraftItemGoal(ItemType item)
//     {
//         Name = $"CraftItem:{item}";
//         itemType = item;
//     }

//     public override float CalculateUtility(GameData gameData, int playerId)
//     {
//         int currentAmount = gameData.PlayerItems[playerId][itemType];
//         // The less we have, the higher the utility
//         return 100 - currentAmount;
//     }

//     public override HashSet<string> GetGoalState()
//     {
//         return new HashSet<string> { $"HasItem:{itemType}" };
//     }
// }

// public class TrainUnitGoal : Goal
// {
//     private UnitType unitType;

//     public TrainUnitGoal(UnitType unit)
//     {
//         Name = $"TrainUnit:{unit}";
//         unitType = unit;
//     }

//     public override float CalculateUtility(GameData gameData, int playerId)
//     {
//         int unitCount = gameData.GetUnitCount(playerId, unitType);
//         // The less we have, the higher the utility
//         return 50 - unitCount * 10;
//     }

//     public override HashSet<string> GetGoalState()
//     {
//         return new HashSet<string> { $"HasUnit:{unitType}" };
//     }
// }

// public class DefendGoal : Goal
// {
//     public DefendGoal()
//     {
//         Name = "Defend";
//     }

//     public override float CalculateUtility(GameData gameData, int playerId)
//     {
//         return gameData.IsPlayerUnderAttack(playerId) ? 90.0f : 0.0f;
//     }

//     public override HashSet<string> GetGoalState()
//     {
//         return new HashSet<string> { "Safe" };
//     }
// }

// #endregion

// #region GOAPPlanner Class

// public class GOAPPlanner
// {
//     public Queue<Action> Plan(HashSet<string> worldState, HashSet<string> goalState, List<Action> availableActions)
//     {
//         var openList = new PriorityQueue<Node>();
//         var closedList = new HashSet<HashSet<string>>(HashSet<string>.CreateSetComparer());

//         var startNode = new Node(null, 0, worldState, null);
//         openList.Enqueue(startNode, 0);

//         while (openList.Count > 0)
//         {
//             var currentNode = openList.Dequeue();

//             if (GoalAchieved(goalState, currentNode.State))
//             {
//                 return BuildPlan(currentNode);
//             }

//             closedList.Add(currentNode.State);

//             foreach (var action in availableActions)
//             {
//                 if (action.ArePreconditionsMet(currentNode.State))
//                 {
//                     var newState = action.ApplyEffects(currentNode.State);
//                     if (closedList.Any(s => s.SetEquals(newState)))
//                         continue;

//                     var newNode = new Node(currentNode, currentNode.Cost + action.Cost, newState, action);
//                     var heuristic = Heuristic(newState, goalState);
//                     openList.Enqueue(newNode, newNode.Cost + heuristic);
//                 }
//             }
//         }

//         // No plan found
//         return null;
//     }

//     private bool GoalAchieved(HashSet<string> goalState, HashSet<string> currentState)
//     {
//         return goalState.IsSubsetOf(currentState);
//     }

//     private float Heuristic(HashSet<string> state, HashSet<string> goal)
//     {
//         return goal.Count - state.Intersect(goal).Count();
//     }

//     private Queue<Action> BuildPlan(Node node)
//     {
//         var plan = new Stack<Action>();
//         while (node != null && node.Action != null)
//         {
//             plan.Push(node.Action);
//             node = node.Parent;
//         }
//         return new Queue<Action>(plan);
//     }

//     private class Node
//     {
//         public Node Parent { get; }
//         public float Cost { get; }
//         public HashSet<string> State { get; }
//         public Action Action { get; }

//         public Node(Node parent, float cost, HashSet<string> state, Action action)
//         {
//             Parent = parent;
//             Cost = cost;
//             State = state;
//             Action = action;
//         }
//     }
// }

// #endregion

// #region PriorityQueue Class

// public class PriorityQueue<T>
// {
//     private List<KeyValuePair<T, float>> elements = new List<KeyValuePair<T, float>>();

//     public int Count => elements.Count;

//     public void Enqueue(T item, float priority)
//     {
//         elements.Add(new KeyValuePair<T, float>(item, priority));
//     }

//     public T Dequeue()
//     {
//         // Get the item with the lowest priority
//         int bestIndex = 0;
//         float bestPriority = elements[0].Value;

//         for (int i = 1; i < elements.Count; i++)
//         {
//             if (elements[i].Value < bestPriority)
//             {
//                 bestPriority = elements[i].Value;
//                 bestIndex = i;
//             }
//         }

//         T bestItem = elements[bestIndex].Key;
//         elements.RemoveAt(bestIndex);
//         return bestItem;
//     }
// }

// #endregion

// #region GameData and Supporting Classes

// public class GameData
// {
//     public Dictionary<int, Dictionary<ResourceType, int>> PlayerResources { get; private set; }
//     public Dictionary<int, Dictionary<ItemType, int>> PlayerItems { get; private set; }
//     public Dictionary<int, List<Building>> PlayerBuildings { get; private set; }
//     public Dictionary<int, List<Unit>> PlayerUnits { get; private set; }

//     public GameData()
//     {
//         PlayerResources = new Dictionary<int, Dictionary<ResourceType, int>>();
//         PlayerItems = new Dictionary<int, Dictionary<ItemType, int>>();
//         PlayerBuildings = new Dictionary<int, List<Building>>();
//         PlayerUnits = new Dictionary<int, List<Unit>>();
//     }

//     public bool IsPlayerUnderAttack(int playerId)
//     {
//         // Implement logic to determine if player is under attack
//         return false;
//     }

//     public int GetUnitCount(int playerId, UnitType unitType)
//     {
//         if (PlayerUnits.ContainsKey(playerId))
//         {
//             return PlayerUnits[playerId].Count(u => u.UnitType == unitType);
//         }
//         return 0;
//     }

//     public void ExecuteAction(int playerId, Action action)
//     {
//         // Implement the action effects in the game world
//         if (action.Name.StartsWith("GatherResource:"))
//         {
//             var resourceName = action.Name.Split(':')[1];
//             var resourceType = (ResourceType)Enum.Parse(typeof(ResourceType), resourceName);
//             PlayerResources[playerId][resourceType] += 10; // Add resource
//         }
//         else if (action.Name.StartsWith("CraftItem:"))
//         {
//             var itemName = action.Name.Split(':')[1];
//             var itemType = (ItemType)Enum.Parse(typeof(ItemType), itemName);

//             // Consume required resource
//             var requiredResource = ActionFactory.GetRequiredResource(itemType);
//             if (PlayerResources[playerId][requiredResource] >= 5)
//             {
//                 PlayerResources[playerId][requiredResource] -= 5;
//                 PlayerItems[playerId][itemType] += 1;
//             }
//         }
//         else if (action.Name.StartsWith("ConstructBuilding:"))
//         {
//             var buildingName = action.Name.Split(':')[1];
//             var buildingType = (BuildingType)Enum.Parse(typeof(BuildingType), buildingName);

//             // Consume required items
//             var requiredItems = ActionFactory.GetRequiredItemsForBuilding(buildingType);
//             bool hasAllItems = requiredItems.All(item => PlayerItems[playerId][item] > 0);

//             if (hasAllItems)
//             {
//                 foreach (var item in requiredItems)
//                 {
//                     PlayerItems[playerId][item] -= 1;
//                 }
//                 PlayerBuildings[playerId].Add(new Building { BuildingType = buildingType });
//             }
//         }
//         else if (action.Name.StartsWith("TrainUnit:"))
//         {
//             var unitName = action.Name.Split(':')[1];
//             var unitType = (UnitType)Enum.Parse(typeof(UnitType), unitName);

//             // Consume required item
//             var requiredItem = ActionFactory.GetRequiredItemForUnit(unitType);
//             if (PlayerItems[playerId][requiredItem] >= 1)
//             {
//                 PlayerItems[playerId][requiredItem] -= 1;
//                 PlayerUnits[playerId].Add(new Unit { UnitType = unitType });
//             }
//         }
//         else if (action.Name == "Defend")
//         {
//             // Implement defend logic
//             // For simplicity, assume the player is now safe
//             // This should update the game state accordingly
//         }
//     }
// }

// public class Building
// {
//     public BuildingType BuildingType { get; set; }
// }

// public class Unit
// {
//     public UnitType UnitType { get; set; }
// }

// #endregion

// #region AIAgent Class

// public class AIAgent
// {
//     private int playerId;
//     private List<Goal> goals;
//     private List<Action> availableActions;
//     private GOAPPlanner planner;
//     private WorldState worldState;
//     private GameData gameData;

//     public AIAgent(int playerId, GameData gameData)
//     {
//         this.playerId = playerId;
//         this.gameData = gameData;
//         worldState = new WorldState();
//         planner = new GOAPPlanner();
//         InitializeGoals();
//     }

//     private void InitializeGoals()
//     {
//         goals = new List<Goal>
//         {
//             new DefendGoal(),
//             new ConstructBuildingGoal(BuildingType.LumberjackHut),
//             new ConstructBuildingGoal(BuildingType.Sawmill),
//             new ConstructBuildingGoal(BuildingType.StoneQuarry),
//             new ConstructBuildingGoal(BuildingType.StoneMason),
//             new ConstructBuildingGoal(BuildingType.Barracks),
//             new GatherResourceGoal(ResourceType.Wood),
//             new GatherResourceGoal(ResourceType.Stone),
//             new CraftItemGoal(ItemType.Planks),
//             new CraftItemGoal(ItemType.StoneBlocks),
//             new TrainUnitGoal(UnitType.Warrior)
//         };
//     }

//     public void Update()
//     {
//         // Update world state based on game data
//         worldState.UpdateWorldState(gameData, playerId);

//         // Determine best goal
//         var bestGoal = DetermineBestGoal();
//         if (bestGoal == null)
//         {
//             // No achievable goals
//             return;
//         }

//         // Get available actions
//         availableActions = ActionFactory.GetAvailableActions(gameData, playerId);

//         // Plan to achieve the goal
//         var plan = planner.Plan(worldState.Conditions, bestGoal.GetGoalState(), availableActions);

//         if (plan == null)
//         {
//             // No plan found
//             return;
//         }

//         // Execute the plan
//         ExecutePlan(plan);
//     }

//     private Goal DetermineBestGoal()
//     {
//         Goal bestGoal = null;
//         float highestUtility = float.MinValue;

//         foreach (var goal in goals)
//         {
//             var utility = goal.CalculateUtility(gameData, playerId);

//             if (utility > highestUtility)
//             {
//                 highestUtility = utility;
//                 bestGoal = goal;
//             }
//         }

//         return bestGoal;
//     }

//     private void ExecutePlan(Queue<Action> plan)
//     {
//         while (plan.Count > 0)
//         {
//             var action = plan.Dequeue();
//             PerformAction(action);
//         }
//     }

//     private void PerformAction(Action action)
//     {
//         // Implement the logic to perform the action in the game
//         gameData.ExecuteAction(playerId, action);
//     }
// }

// #endregion

// #region GameLoop Class

// public class GameLoop
// {
//     private AIAgent aiAgent;
//     private GameData gameData;
//     private int playerId = 1;

//     public GameLoop(PlayerData player)
//     {
//         gameData = new GameData();

//         // Initialize game data for the player
//         InitializePlayerData(player.Id);

//         aiAgent = new AIAgent(player.Id, gameData);
//     }

//     private void InitializePlayerData(int playerId)
//     {
//         // Initialize resources
//         gameData.PlayerResources[playerId] = new Dictionary<ResourceType, int>
//         {
//             { ResourceType.Wood, 0 },
//             { ResourceType.Stone, 0 }
//         };

//         // Initialize items
//         gameData.PlayerItems[playerId] = new Dictionary<ItemType, int>
//         {
//             { ItemType.Planks, 0 },
//             { ItemType.StoneBlocks, 0 }
//         };

//         // Initialize buildings
//         gameData.PlayerBuildings[playerId] = new List<Building>();

//         // Initialize units
//         gameData.PlayerUnits[playerId] = new List<Unit>
//         {
//             new Unit { UnitType = UnitType.Worker }
//         };
//     }

//     public void Update()
//     {
//         // Game logic...

//         // Update AI
//         aiAgent.Update();

//         // Other game updates...
//     }
// }

// #endregion
