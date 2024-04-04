using UnityEngine;

public partial class PlayerAI
{
    /*
        Goal 1: Ensure a steady stream of resources to build more buildings and units
            prereq: need to have workers in gatherer-building next to resource node
                optimize: the "right #" of workers to maximize resource gathering
                => prereq: need to have gatherer-building that can gather resources from resource nodes and have a path to the resource nodes
                    optimize: closer = better
                => prereq: need to be able to construct gatherer-building in at least one node next to resource node
                    => prereq: need to own all nodes between that node and closest owned node
                        => ...
                    => prereq: an enemy must not own the node
                        => ...
                    => prereq: the resources needed to build the gatherer-building must be available and a path to the node must be available from where the node(s) that the resources are in
                        => ...


    Strategies
        early game
            Build stable economy, gradually expand.
            Zerg rush
            variables
                DesireToExpand: how quickly the AI tries to expand;
                    range = prioritizes grabbing node w/o necessarily being able to defend them, or prioritizes building up defenses before expanding
                    quickly tries to expand new bases
                    quickly tries to capture resource nodes to expand economy
                DesireToAttack: how quickly the AI tries to attack
                    range = prioritizes building up defenses before attacking, or prioritizes attacking before building up defenses
                    zerg vs turtle
                    0 desire = fully peaceful NPC; 1 desire = fully aggressive NPC

            Mindsets
                Expansionist
                    main goal: expand to nodes that provide resources that allow further expansion.
                    
                Economist
                Warmonger
                Turtler
                Zerg
                Balanced
                Random

    */

}