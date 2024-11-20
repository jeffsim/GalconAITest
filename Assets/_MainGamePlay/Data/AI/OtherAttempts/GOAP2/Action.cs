using System.Collections.Generic;

public abstract class Action2
{
    public string Name { get; set; }
    public HashSet<string> Preconditions { get; set; }
    public HashSet<string> Effects { get; set; }
    public float Cost { get; set; }

    public Action2()
    {
        Preconditions = new HashSet<string>();
        Effects = new HashSet<string>();
    }

    public virtual bool ArePreconditionsMet(HashSet<string> state)
    {
        return Preconditions.IsSubsetOf(state);
    }

    public virtual HashSet<string> ApplyEffects(HashSet<string> state)
    {
        var newState = new HashSet<string>(state);
        foreach (var effect in Effects)
        {
            newState.Add(effect);
        }
        return newState;
    }

    public abstract void Perform(AI_TownState townState, int playerId);
}