using Godot;
using System;


// To use this plugin, you should inherit this class to add scripts to your nodes
// This kind of an implementation https://statecharts.github.io
// The two main differences with a classic fsm are:
// The composition of states and substates
// The regions (sibling states that stay active at the same time)
//
// Your script can implement those virtual functions:
//  public override void _OnEnter()
//  public override void _AfterEnter()
//  public override void _OnUpdate(_delta)
//  public override void _AfterUpdate(_delta)
//  public override void _BeforeExit()
//  public override void _OnExit()
//  public override void _OnTimeout(_name)
//
// Call a method to your State in the intended track of AnimationPlayer
// if you want to act (ie change State) after or during an animation
//
// In those scripts, you can call the public functions:
//  ChangeState("MyState")
//    "MyState" is the name of an existing Node State
//  IsActive("MyState") -> bool
//    returns true if a state "MyState" is active in this xsm
//  GetActiveStates() -> Dictionary:
//    returns a dictionary with all the active States
//  GetState("MyState) -> State
//    returns the State Node "MyState". You have to specify "Parent/MyState" if
//    "MyState" is not a unique name
//  Play("Anim")
//    plays the animation "Anim" of the State's AnimationPlayer
//  Stop()
//    stops the current animation
//  IsPlaying("Anim)
//    returns true if "Anim" is playing
//  AddTimer("Name", time)
//    adds a timer named "Name" and returns this timer
//    when the time is out, the function _on_timeout(_name) is called
//  DelTimer("Name")
//    deletes the timer "Name"
//  IsTimer("Name")
//    returns true if there is a Timer "Name" running in this State
//  GetActiveSubstate()
//    returns the active substate (all the children if has_regions)



[Tool]
public class State : Node
{
    [Signal]
    public delegate void StateEntered(State sender);

    [Signal]
    public delegate void StateExited(State sender);

    [Signal]
    public delegate void StateUpdated(State sender);

    [Signal]
    public delegate void StateChanged(State sender, string newState);

    [Signal]
    public delegate void SubstateEntered(State sender);

    [Signal]
    public delegate void SubstateExited(State sender);

    [Signal]
    public delegate void SubstateChanged(State sender, string newState);

    [Signal]
    public delegate void Disabled();

    [Signal]
    public delegate void Enabled();


    public bool disabled;
    [Export]
    bool _disabled
    {
        get { return disabled; }
        set { disabled = value; SetDisabled(value); }
    }

    [Export]
    bool hasRegions = false,
         debugMode = false;

    [Export]
    public NodePath fsmOwner = null,
                    animationPlayer = null;

    public const int INACTIVE = 0,
                     ENTERING = 1,
                     ACTIVE = 2,
                     EXITING = 3;

    public int status = INACTIVE;
    public StateRoot stateRoot = null;
    public Node target = null;

    AnimationPlayer animPlayer = null;
    State lastState = null;
    bool doneForThisFrame = false;
    public bool stateInUpdate = false;

    //
    // INIT
    //
    public override void _Ready()
    {
        if (Engine.EditorHint)
        {
            return;
        }

        if (fsmOwner != null)
        {
            target = GetNode(fsmOwner);
        }

        if (animationPlayer != null)
        {
            animPlayer = GetNode<AnimationPlayer>(animationPlayer);
        }
    }


    public override string _GetConfigurationWarning()
    {
        foreach (var c in GetChildren())
        {
            string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);

            if (nodeType != "State")
            {
                Node pNode = (Node)c;
                return $"Error : this Node has a non-State child ({pNode.Name} : {pNode.GetClass()} )";
            }
        }
        return "";
    }

    public static bool HasMethod(object objectToCheck, string methodName)
    {
        var type = objectToCheck.GetType();
        return type.GetMethod(methodName) != null;
    }

    //
    // FUNCTIONS TO INHERIT
    //
    public virtual void _OnEnter(string _args) { }

    public virtual void _AfterEnter(string _args) { }

    public virtual void _OnUpdate(float _delta) { }

    public virtual void _AfterUpdate(float _delta) { }

    public virtual void _BeforeExit(string _args) { }

    public virtual void _OnExit(string _args) { }

    public virtual void _OnTimeout(string _name) { }


    //
    // FUNCTIONS TO CALL IN INHERITED STATES
    //
    public State ChangeState(string newState,
                             string argsOnEnter = null,
                             string argsAfterEnter = null,
                             string argsBeforeExit = null,
                             string argsOnExit = null)
    {
        if (!stateRoot.stateInUpdate)
        {
            if (debugMode)
            {
                GD.Print($"{target.Name} pending state :{this.Name} -> {newState}");
            }
            stateRoot.NewPendingState(newState, argsOnEnter, argsAfterEnter, argsBeforeExit, argsOnExit);
            return null;
        }

        if (doneForThisFrame)
        {
            return null;
        }

        // if change to empty or itself, cancel
        if ((newState == "") || (newState == this.Name))
        {
            return null;
        }

        // finds the path to next state, return if null, disabled or active
        State newStateNode = FindStateNode(newState);
        if (newStateNode == null)
        {
            return null;
        }
        if (newStateNode.disabled)
        {
            return null;
        }
        if (newStateNode.status != INACTIVE)
        {
            return null;
        }

        if (debugMode)
        {
            GD.Print($"{target.Name} changing state :{this.Name} -> {newState}");
        }
        // compare the current path and the new one -> get the common_root
        State commonRoot = GetCommonRoot(newStateNode);

        // change the children status to EXITING
        commonRoot.ChangeChildrenStatusToExiting();
        // exits all active children of the old branch,
        // from farthest to common_root (excluded)
        // If EXITED, change the status to INACTIVE
        commonRoot.ExitChildren(argsBeforeExit, argsOnExit);

        // change the children status to ENTERING
        commonRoot.ChangeChildrenStatusToEntering(newStateNode.GetPath());
        // enters the nodes of the new branch from the parent to the next_state
        // enters the first leaf of each following branch
        // If ENTERED, change the status to ACTIVE
        commonRoot.EnterChildren(argsOnEnter, argsAfterEnter);

        // sets this State as last_state for the new one
        newStateNode.lastState = this;

        // set "done this frame" to avoid another round of state change in this branch
        commonRoot.ResetDoneThisFrame(true);

        // signal the change
        EmitSignal(nameof(StateChanged), this, newState);
        if (!IsRoot())
        {
            newStateNode.GetParent().EmitSignal(nameof(SubstateChanged), this, newStateNode);
        }
        stateRoot.EmitSignal(nameof(StateRoot.SomeStateChanged), this, newStateNode);

        if (debugMode)
        {
            GD.Print($"{target.Name} changed state : {this.Name} -> {newState}");
        }

        return newStateNode;
    }


    // New function name
    void GotoState(string newState)
    {
        ChangeState(newState);
    }

    State ChangeStateIf(string newState, string ifState)
    {
        State s = FindStateNode(ifState);
        if ((s == null) || (s.status == ACTIVE))
        {
            return ChangeState(newState);
        }
        return null;
    }


    bool IsActive(string stateName)
    {
        State s = FindStateNode(stateName);
        if (s == null)
        {
            return false;
        }
        return s.status == ACTIVE;
    }


    bool WasActive(string stateName, int historyId = 0)
    {
        return stateRoot.WasStateActive(stateName, historyId);
    }

    // returns the first active substate or all children if has_regions
    Godot.Collections.Array GetActiveSubstate()
    {
        if (hasRegions && (status == ACTIVE))
        {
            return GetChildren();
        }
        else
        {
            foreach (State c in GetChildren())
            {
                if (c.status == ACTIVE)
                {
                    var result = new Godot.Collections.Array { c };
                    return result;
                }

            }
        }
        return null;
    }


    State GetState(string stateName)
    {
        return FindStateNode(stateName);
    }


    Godot.Collections.Dictionary<string, State> GetActiveStates()
    {
        return stateRoot.activeStates;
    }


    void Play(string anim, float customSpeed = 1.0f, bool fromEnd = false)
    {
        if ((status == ACTIVE) && (animPlayer != null) && animPlayer.HasAnimation(anim))
        {
            if (animPlayer.CurrentAnimation != anim)
            {
                animPlayer.Stop();
                animPlayer.Play(anim);
            }
        }
    }


    void PlayBackwards(string anim)
    {
        Play(anim, -1.0f, true);
    }

    void PlayBlend(string anim, float customBlend, float customSpeed = 1.0f, bool fromEnd = false)
    {
        if ((status == ACTIVE) && (animPlayer != null) && animPlayer.HasAnimation(anim))
        {
            if (animPlayer.CurrentAnimation != anim)
            {
                animPlayer.Play(anim, customBlend, customSpeed, fromEnd);
            }
        }

    }


    void PlaySync(string anim, float customSpeed = 1.0f, bool fromEnd = false)
    {
        if ((status == ACTIVE) && (animPlayer != null) && animPlayer.HasAnimation(anim))
        {
            string currAnim = animPlayer.CurrentAnimation;
            if ((currAnim != anim) && (currAnim != ""))
            {
                float currAnimPos = animPlayer.CurrentAnimationPosition;
                float currAnimLength = animPlayer.CurrentAnimationLength;
                float ratio = currAnimPos / currAnimLength;
                Play(anim, customSpeed, fromEnd);
                animPlayer.Seek(ratio * animPlayer.CurrentAnimationLength);
            }
            else
            {
                Play(anim, customSpeed, fromEnd);
            }
        }
    }


    void Pause()
    {
        Stop(false);
    }


    void Queue(string anim)
    {
        if ((status == ACTIVE) && (animPlayer != null) && (animPlayer.HasAnimation(anim)))
        {
            animPlayer.Queue(anim);
        }
    }


    void Stop(bool reset = true)
    {
        if ((status == ACTIVE) && (animPlayer != null))
        {
            animPlayer.Stop(reset);
        }

    }


    bool IsPlaying(string anim)
    {
        if (animPlayer != null)
        {
            return animPlayer.CurrentAnimation == anim;
        }

        return false;
    }


    Godot.Timer AddTimer(string name, float time)
    {
        DelTimer(name);
        var timer = new Godot.Timer();
        AddChild(timer);
        timer.Name = name;
        timer.OneShot = true;
        timer.Start(time);
        timer.Connect("timeout", this, nameof(_OnTimeout), new Godot.Collections.Array { name });
        return timer;
    }


    void DelTimer(string name)
    {
        if (HasNode(name))
        {
            Timer timer = (Timer)GetNode(name);
            timer.Stop();
            timer.QueueFree();
            timer.Name = "to_delete";
        }
    }


    void DelTimers()
    {
        foreach (var c in GetChildren())
        {
            string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);

            if (nodeType == "Timer")
            {
                Timer cTimer = (Timer)c;

                cTimer.Stop();
                cTimer.QueueFree();
                cTimer.Name = "to_delete";
            }
        }
    }


    bool HasTimer(string name)
    {
        return HasNode(name);
    }



    //
    // PROTECTED FU*NCTIONS
    //
    // Careful, if your substates have the same name,
    // their parents names must be different
    // It would be easier if the state_root name is unique
    protected void InitChildrenStateMap(Godot.Collections.Dictionary<string, State> dict, StateRoot newStateRoot)
    {
        stateRoot = newStateRoot;

        foreach (State c in GetChildren())
        {
            if (dict.ContainsKey(c.Name))
            {
                State currState = dict[c.Name];
                State currParent = (State)currState.GetParent();
                dict.Remove(c.Name);
                dict[$"{currParent.Name}/{c.Name}"] = currState;
                dict[$"{this.Name}/{c.Name}"] = c;
                stateRoot.duplicateNames[c.Name] = 1;
            }
            else if (stateRoot.duplicateNames.ContainsKey(c.Name))
            {
                dict[$"{this.Name}/{c.Name}"] = c;
                stateRoot.duplicateNames[c.Name] += 1;
            }
            else
            {
                dict[c.Name] = c;
            }

            c.InitChildrenStateMap(dict, stateRoot);
        }
    }

    // TODO: fix this StateRoot arg (should be State)
    protected void InitChildrenStates(State rootState, bool firstBranch)
    {
        foreach (State c in GetChildren())
        {
            string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);
            if (nodeType != "State")
            {
                continue;
            }

            c.status = INACTIVE;
            c.stateRoot = (StateRoot)rootState;
            if (c.target == null)
            {
                c.target = rootState.target;
            }
            if (c.animPlayer == null)
            {
                c.animPlayer = rootState.animPlayer;
            }
            if (firstBranch && (hasRegions || (c == GetChild(0))))
            {
                c.status = ACTIVE;
                c.Enter();
                c.lastState = rootState;
                c.InitChildrenStates(rootState, true);
                c._AfterEnter(null);
            }
            else
            {
                c.InitChildrenStates(rootState, false);
            }
        }
    }


    protected void UpdateActiveStates(float _delta)
    {
        if (disabled)
        {
            return;
        }
        stateInUpdate = true;
        Update(_delta);
        foreach (State c in GetChildren())
        {
            string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);
            if ((nodeType == "State") && (c.status == ACTIVE) && (!c.doneForThisFrame))
            {
                c.UpdateActiveStates(_delta);
            }
        }
        _AfterUpdate(_delta);
        stateInUpdate = false;
    }


    protected void ResetDoneThisFrame(bool newDone)
    {
        doneForThisFrame = newDone;
        if (!IsAtomic())
        {
            foreach (State c in GetChildren())
            {
                string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);
                if (nodeType == "State")
                {
                    c.ResetDoneThisFrame(newDone);
                }
            }
        }
    }


    protected void Enter(string args = null)
    {
        if (disabled)
        {
            return;
        }
        status = ACTIVE;
        stateRoot.AddActiveState(this);
        _OnEnter(args);
        EmitSignal(nameof(StateEntered), this);
        if (!IsRoot())
        {
            GetParent().EmitSignal(nameof(SubstateEntered), this);
        }
    }


    //
    // PRIVATE FUNCTIONS
    //
    void SetDisabled(bool newDisabled)
    {
        disabled = newDisabled;
        if (disabled)
        {
            EmitSignal(nameof(Disabled));
        }
        else
        {
            EmitSignal(nameof(Enabled));
        }

        SetDisabledChildren(newDisabled);
    }


    void SetDisabledChildren(bool newDisabled)
    {
        foreach (State c in GetChildren())
        {
            c.SetDisabled(newDisabled);
        }
    }


    State GetCommonRoot(State newStateNode)
    {
        NodePath newPath = newStateNode.GetPath(); // huh
        State result = newStateNode;
        while (!(result.status == ACTIVE) && !(result.IsRoot()))
        {
            result = (State)result.GetParent();
        }
        return result;
    }


    void Update(float _delta)
    {
        if (status == ACTIVE)
        {
            _OnUpdate(_delta);
            EmitSignal(nameof(StateUpdated), this);
        }
    }


    void ChangeChildrenStatusToEntering(NodePath newStatePath)
    {
        if (hasRegions)
        {
            foreach (State c in GetChildren())
            {
                c.status = ENTERING;
                c.ChangeChildrenStatusToEntering(newStatePath);
                return;
            }
        }

        int newStateLvl = newStatePath.GetNameCount();
        int currentLvl = GetPath().GetNameCount();
        if (newStateLvl > currentLvl)
        {
            foreach (State c in GetChildren())
            {
                string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);
                string currentName = newStatePath.GetName(currentLvl);
                if ((nodeType == "State") && (c.Name == currentName))
                {
                    c.status = ENTERING;
                    c.ChangeChildrenStatusToEntering(newStatePath);
                }
            }
        }
        else
        {
            if (GetChildCount() > 0)
            {
                State c = (State)GetChild(0);
                string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);
                if (nodeType == "State")
                {
                    c.status = ENTERING;
                    c.ChangeChildrenStatusToEntering(newStatePath);
                }
            }
        }
    }


    void EnterChildren(string argsOnEnter = null, string argsAfterEnter = null)
    {
        if (disabled)
        {
            return;
        }
        // enter all ENTERING substates
        foreach (State c in GetChildren())
        {
            string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);
            if ((nodeType == "State") && (c.status == ENTERING))
            {
                c.Enter(argsOnEnter);
                c.EnterChildren(argsOnEnter, argsAfterEnter);
                c._AfterEnter(argsAfterEnter);
            }
        }
    }


    void Exit(string _args = null)
    {
        DelTimers();
        _OnExit(_args);
        status = INACTIVE;
        stateRoot.RemoveActiveState(this);
        EmitSignal(nameof(StateExited), this);
        if (!IsRoot())
        {
            GetParent().EmitSignal(nameof(SubstateExited), this);
        }
    }


    void ChangeChildrenStatusToExiting()
    {
        if (hasRegions)
        {
            foreach (State c in GetChildren())
            {
                c.status = EXITING;
                c.ChangeChildrenStatusToExiting();
            }
        }
        else
        {
            foreach (State c in GetChildren())
            {
                string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);
                if ((nodeType == "State") && (c.status != INACTIVE))
                {
                    c.status = EXITING;
                    c.ChangeChildrenStatusToExiting();
                }
            }
        }
    }


    void ExitChildren(string argsBeforeExit = null, string argsOnExit = null)
    {
        foreach (State c in GetChildren())
        {
            string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);
            if ((nodeType == "State") && (c.status == EXITING))
            {
                c._BeforeExit(argsBeforeExit);
                c.ExitChildren();
                c.Exit(argsOnExit);
            }
        }
    }


    void ResetChildrenStatus()
    {
        foreach (State c in GetChildren())
        {
            string nodeType = (string)c.GetType().GetMethod("GetClass").Invoke(c, null);
            if (nodeType != "State")
            {
                c.status = INACTIVE;
                c.ResetChildrenStatus();
            }
        }
    }


    State FindStateNode(string newState)
    {
        if (this.Name == newState)
        {
            return this;
        }

        Godot.Collections.Dictionary<string, State> stateMap = stateRoot.stateMap;
        if (stateMap.ContainsKey(newState))
        {
            return stateMap[newState];
        }

        if (stateRoot.duplicateNames.ContainsKey(newState))
        {
            if (stateMap.ContainsKey($"{this.Name}/{newState}"))
            {
                return stateMap[$"{this.Name}/{newState}"];
            }
            else if (stateMap.ContainsKey($"{GetParent().Name}/{newState}"))
            {
                return stateMap[$"{GetParent().Name}/{newState}"];
            }
        }

        return null;
    }


    void _OnTimerTimeout(string name)
    {
        DelTimer(name);
        _OnTimeout(name);
    }


    new public string GetClass()
    {
        return "State";
    }


    bool IsAtomic()
    {
        return GetChildCount() == 0;
    }


    bool IsRoot()
    {
        if (this.IsRoot())
        {
            return true;
        }

        return false;
    }
}
