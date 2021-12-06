using Godot;
using System;
using System.Collections.Generic;

// This is the necessary manager node for any XSM. This is a special State that
// can handle change requests outside of XSM's logic. See the example to get it!
// This node will probably expand a bit in the next versions of XSM

[Tool]
public class StateRoot : State
{

    [Signal]
    public delegate void SomeStateChanged(State senderNode, string newStateNode);

    [Signal]
    public delegate void PendingStateChanged(State addedStateNode);

    [Signal]
    public delegate void PendingStateAdded(string newStateName);

    [Signal]
    public delegate void ActiveStateListChanged(Godot.Collections.Dictionary<string, State> activeStatesList);

    [Export(PropertyHint.Range, "0,1024,")]
    int historySize = 12;

    public Godot.Collections.Dictionary<string, State> stateMap = new Godot.Collections.Dictionary<string, State>();
    public Godot.Collections.Dictionary<string, int> duplicateNames = new Godot.Collections.Dictionary<string, int>(); // Stores number of times a state_name is duplicated
    List<string[]> pendingStates = new List<string[]>();

    public Godot.Collections.Dictionary<string, State> activeStates = new Godot.Collections.Dictionary<string, State>();
    List<Godot.Collections.Dictionary<string, State>> activeStatesHistory = new List<Godot.Collections.Dictionary<string, State>>();


    //
    // INIT
    //
    public override void _Ready()
    {
        stateRoot = this;

        if (fsmOwner == null && GetParent() != null)
        {
            target = GetParent();
        }

        InitStateMap();
        Enter();
        InitChildrenStates(this, true);
        _AfterEnter(null);
    }


    public override string _GetConfigurationWarning()
    {
        if (disabled)
        {
            return "Warning : Your root State is disabled. It will not work";
        }
        if (fsmOwner == null)
        {
            return "Warning : Your root State has no target";
        }
        if (animationPlayer == null)
        {
            return "Warning : Your root State has no AnimationPlayer registered";
        }
        return base._GetConfigurationWarning();
    }


    // Careful, if your substates have the same name,
    // their parents'names must be different
    void InitStateMap()
    {
        stateMap["name"] = this;
        InitChildrenStateMap(stateMap, this);
    }


    //
    // PROCESS
    //
    public override void _PhysicsProcess(float _delta)
    {
        if (Engine.EditorHint)
        {
            return;
        }

        if (!disabled && (status == ACTIVE))
        {
            ResetDoneThisFrame(false);
            AddToActiveStatesHistory(activeStates.Duplicate());
            while (pendingStates.Count > 0)
            {
                stateInUpdate = true;
                // grab first element and then remove from list, like pop_front
                string[] newStateWithArgs = pendingStates[0];
                pendingStates.RemoveAt(0);

                string newState = newStateWithArgs[0];
                string arg1 = newStateWithArgs[1];
                string arg2 = newStateWithArgs[2];
                string arg3 = newStateWithArgs[3];
                string arg4 = newStateWithArgs[4];
                State newStateNode = ChangeState(newState, arg1, arg2, arg3, arg4);
                EmitSignal(nameof(PendingStateChanged), newStateNode);
                stateInUpdate = false;
            }
            UpdateActiveStates(_delta);
        }
    }


    //
    // FUNCTIONS TO CALL IN INHERITED STATES
    //
    // Careful, only the last one added in this frame will be change in xsm
    public void NewPendingState(string newStateName,
                                string argsOnEnter = null,
                                string argsAfterEnter = null,
                                string argsBeforeExit = null,
                                string argsOnExit = null)
    {

        string[] newStateArray = new string[5];
        newStateArray[0] = newStateName;
        newStateArray[1] = argsOnEnter;
        newStateArray[2] = argsAfterEnter;
        newStateArray[3] = argsBeforeExit;
        newStateArray[4] = argsOnExit;

        pendingStates.Add(newStateArray);

        EmitSignal(nameof(PendingStateAdded), newStateName);
    }


    //
    // PUBLIC FUNCTIONS
    //
    bool InActiveStates(string stateName)
    {
        return activeStates.ContainsKey(stateName);
    }


    // index 0 is the most recent history
    Godot.Collections.Dictionary<string, State> GetPreviousActiveStates(int historyId = 0)
    {
        if (activeStatesHistory.Count <= historyId)
        {
            return activeStatesHistory[0];
        }
        return activeStatesHistory[historyId];
    }


    public bool WasStateActive(string stateName, int historyId = 0)
    {
        Godot.Collections.Dictionary<string, State> prev = GetPreviousActiveStates(historyId);
        if (prev == null)
        {
            return false;
        }
        return GetPreviousActiveStates(historyId).ContainsKey(stateName);
    }


    bool IsRoot()
    {
        return true;
    }


    //
    // PRIVATE FUNCTIONS
    //
    void AddToActiveStatesHistory(Godot.Collections.Dictionary<string, State> newActiveStates)
    {
        activeStatesHistory.Insert(0, newActiveStates);

        if (activeStatesHistory.Count > historySize)
        {
            activeStatesHistory.RemoveAt(activeStatesHistory.Count - 1);
        }
    }


    public void RemoveActiveState(State stateToErase)
    {
        string stateName = stateToErase.Name;
        string nameInStateMap = stateName;
        if (!stateMap.ContainsKey(stateName))
        {
            string parentName = stateToErase.GetParent().Name;
            nameInStateMap = $"{parentName}/{stateName}";
        }
        activeStates.Remove(nameInStateMap);
        EmitSignal(nameof(ActiveStateListChanged), activeStates);
    }


    public void AddActiveState(State stateToAdd)
    {
        string stateName = stateToAdd.Name;
        string nameInStateMap = stateName;
        if (!stateMap.ContainsKey(stateName))
        {
            string parentName = stateToAdd.GetParent().Name;
            nameInStateMap = $"{parentName}/{stateName}";
        }
        activeStates[nameInStateMap] = stateToAdd;
        EmitSignal(nameof(ActiveStateListChanged), activeStates);
    }
}
