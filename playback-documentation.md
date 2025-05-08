# Robots Grasshopper - Simulation Playback Documentation

This document explains the implementation of the Playback button in the Program simulation component of the Robots Grasshopper plugin. The playback functionality allows users to animate robot programs, visualizing how robots execute their movements over time.

## Overview

The Program simulation component has a "Playback" button that opens a control panel for playing, stopping, and adjusting the playback speed of robot program simulations. The implementation consists of several interconnected classes:

1. `Simulation` class - Core component that maintains state and handles program animation
2. `SimulationForm` class - User interface for controlling the simulation 
3. `ComponentButton` class - Custom Grasshopper component attribute that adds a button to a component

## Implementation Structure

### 1. Simulation Component

The `Simulation` class (located in `src/Robots.Grasshopper/Program/Simulation.cs`) is the main Grasshopper component that handles program simulation. It:

- Takes a program, time value, and normalization flag as inputs
- Outputs robot meshes, joint rotations, TCP position, target index, time, program, and errors
- Contains the playback control logic and time management
- Has a custom attribute that adds a "Playback" button

```csharp
public sealed class Simulation : GH_Component
{
    SimulationForm? _form;
    DateTime? _lastTime;
    double _time;
    double _lastInputTime;

    internal double Speed = 1;

    public Simulation() : base("Program simulation", "Sim", 
        "Rough simulation of the robot program, right click for playback controls", 
        "Robots", "Components") { }

    // Other component methods...

    // Creates a custom button attribute for the component
    public override void CreateAttributes()
    {
        m_attributes = new ComponentButton(this, "Playback", ToggleForm);
    }

    // Toggles the playback control form
    void ToggleForm()
    {
        _form ??= new SimulationForm(this);
        _form.Visible = !_form.Visible;

        if (!_form.Visible)
            Stop();
    }

    // Play/pause toggle
    internal void TogglePlay()
    {
        if (_lastTime is null)
        {
            _lastTime = DateTime.Now;
            ExpireSolution(true);
        }
        else
        {
            Pause();
        }
    }

    // Stop the simulation
    internal void Stop()
    {
        Pause();
        _time = _lastInputTime;
        ExpireSolution(true);
    }

    // Pause the simulation
    void Pause()
    {
        if (_form is not null)
            _form.Play.Checked = false;

        _lastTime = null;
    }

    // Update time and re-solve component
    void Update()
    {
        if (_lastTime is null)
            return;

        var currentTime = DateTime.Now;
        TimeSpan delta = currentTime - _lastTime.Value;
        _lastTime = currentTime;
        _time += delta.TotalSeconds * Speed;
        ExpireSolution(true);
    }
}
```

### 2. Simulation Form

The `SimulationForm` class (located in `src/Robots.Grasshopper/Program/SimulationForm.cs`) provides the user interface for controlling simulation playback. It:

- Creates a form with play, stop, and speed control
- Communicates with the main Simulation component
- Handles user interaction events

```csharp
class SimulationForm : ComponentForm
{
    readonly Simulation _component;

    internal readonly CheckBox Play;

    public SimulationForm(Simulation component)
    {
        _component = component;

        Title = "Playback";
        MinimumSize = new Size(0, 200);

        Padding = new Padding(5);

        var font = new Font(FontFamilies.Sans, 14, FontStyle.None, FontDecoration.None);
        var size = new Size(35, 35);

        // Play button (checkbox that looks like a button)
        Play = new CheckBox
        {
            Text = "\u25B6",  // Unicode triangle play symbol
            Size = size,
            Font = font,
            Checked = false,
            TabIndex = 0
        };

        Play.CheckedChanged += (s, e) => component.TogglePlay();

        // Stop button
        var stop = new Button
        {
            Text = "\u25FC",  // Unicode stop symbol
            Size = size,
            Font = font,
            TabIndex = 1
        };

        stop.Click += (s, e) => component.Stop();

        // Speed slider
        var slider = new Slider
        {
            Orientation = Orientation.Vertical,
            Size = new Size(-1, -1),
            TabIndex = 2,
            MaxValue = 400,
            MinValue = -200,
            TickFrequency = 100,
            SnapToTick = true,
            Value = 100,
        };

        slider.ValueChanged += (s, e) => component.Speed = (double)slider.Value / 100.0;

        var speedLabel = new Label
        {
            Text = "100%",
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Layout the controls
        var layout = new DynamicLayout();
        layout.BeginVertical();
        layout.AddSeparateRow(padding: new Padding(10), spacing: new Size(10, 0), controls: [Play, stop]);
        layout.BeginGroup("Speed");
        layout.AddSeparateRow(slider, speedLabel);
        layout.EndGroup();
        layout.EndVertical();

        Content = layout;
    }

    // Stop simulation when the form is closing
    protected override void OnClosing(CancelEventArgs e)
    {
        _component.Stop();
        base.OnClosing(e);
    }
}
```

### 3. Component Button

The `ComponentButton` class (located in `src/Robots.Grasshopper/ComponentButton.cs`) is a custom `GH_ComponentAttributes` implementation that adds a button to a Grasshopper component. It:

- Draws a button below the component
- Handles mouse events
- Executes an action when clicked

```csharp
class ComponentButton(GH_Component owner, string label, Action action) : GH_ComponentAttributes(owner)
{
    const int _buttonSize = 18;

    readonly string _label = label;
    readonly Action _action = action;

    RectangleF _buttonBounds;
    bool _mouseDown;

    // Layout the component and button
    protected override void Layout()
    {
        base.Layout();

        const int margin = 3;

        var bounds = GH_Convert.ToRectangle(Bounds);
        var button = bounds;

        button.X += margin;
        button.Width -= margin * 2;
        button.Y = bounds.Bottom;
        button.Height = _buttonSize;

        bounds.Height += _buttonSize + margin;

        Bounds = bounds;
        _buttonBounds = button;
    }

    // Render the component and button
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        base.Render(canvas, graphics, channel);

        if (channel == GH_CanvasChannel.Objects)
        {
            var prototype = GH_FontServer.StandardAdjusted;
            var font = GH_FontServer.NewFont(prototype, 6f / GH_GraphicsUtil.UiScale);
            var radius = 3;
            var highlight = !_mouseDown ? 8 : 0;

            using var button = GH_Capsule.CreateTextCapsule(_buttonBounds, _buttonBounds, GH_Palette.Black, _label, font, radius, highlight);
            button.Render(graphics, false, Owner.Locked, false);
        }
    }

    // Handle mouse events
    void SetMouseDown(bool value, GH_Canvas canvas, GH_CanvasMouseEvent e, bool action = true)
    {
        if (Owner.Locked || _mouseDown == value)
            return;

        if (value && e.Button != MouseButtons.Left)
            return;

        if (!_buttonBounds.Contains(e.CanvasLocation))
            return;

        if (_mouseDown && !value && action)
            _action();

        _mouseDown = value;
        canvas.Invalidate();
    }

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        SetMouseDown(true, sender, e);
        return base.RespondToMouseDown(sender, e);
    }

    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        SetMouseDown(false, sender, e);
        return base.RespondToMouseUp(sender, e);
    }

    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        SetMouseDown(false, sender, e, false);
        return base.RespondToMouseMove(sender, e);
    }
}
```

### 4. ComponentForm Base Class

The `ComponentForm` class (located in `src/Robots.Grasshopper/ComponentForm.cs`) is a base class for all forms in the plugin. It:

- Centers the form on the mouse
- Makes the form non-resizable
- Handles closing events

```csharp
class ComponentForm : Form
{
    public ComponentForm()
    {
        Maximizable = false;
        Minimizable = false;
        Resizable = false;
        Topmost = true;
        ShowInTaskbar = true;
        Owner = Rhino.UI.RhinoEtoApp.MainWindow;
    }

    // Center the form on the mouse when it becomes visible
    public override bool Visible
    {
        get => base.Visible;
        set
        {
            if (value)
                CenterOnMouse();

            base.Visible = value;
        }
    }

    void CenterOnMouse()
    {
        var mousePos = Mouse.Position;
        int x = (int)mousePos.X + 20;
        int y = (int)mousePos.Y - MinimumSize.Height / 2;
        Location = new Point(x, y);
    }

    // Hide the form when the user tries to close it
    protected override void OnClosing(CancelEventArgs e)
    {
        Visible = false;
        e.Cancel = true;

        base.OnClosing(e);
    }
}
```

## Simulation Implementation

The `Program` class (in `src/Robots/Program/Program.cs`) contains the core simulation logic:

```csharp
public void Animate(double time, bool isNormalized = true)
{
    if (_simulation is null)
        return;

    _simulation.Step(time, isNormalized);

    if (MeshPoser is null)
        return;

    var current = _simulation.CurrentSimulationPose;
    var systemTarget = Targets[current.TargetIndex];

    MeshPoser.Pose(current.Kinematics, systemTarget);
}
```

The `Simulation` class (in `src/Robots/Program/Simulation.cs`) handles time interpolation:

```csharp
public void Step(double time, bool isNormalized)
{
    if (_keyframes.Count == 1)
        return;

    if (isNormalized) time *= _program.Duration;
    time = Clamp(time, 0, _duration);

    // Find the appropriate keyframes based on time
    if (time >= CurrentSimulationPose.CurrentTime)
    {
        for (int i = _currentTarget; i < _keyframes.Count - 1; i++)
        {
            if (_keyframes[i + 1].TotalTime >= time)
            {
                _currentTarget = i;
                break;
            }
        }
    }
    else
    {
        for (int i = _currentTarget; i >= 0; i--)
        {
            if (_keyframes[i].TotalTime <= time)
            {
                _currentTarget = i;
                break;
            }
        }
    }

    // Interpolate between keyframes
    var systemTarget = _keyframes[_currentTarget + 1];
    var prevSystemTarget = _keyframes[_currentTarget + 0];
    var prevJoints = prevSystemTarget.ProgramTargets.Map(x => x.Kinematics.Joints);

    var kineTargets = systemTarget.Lerp(prevSystemTarget, _program.RobotSystem, time, prevSystemTarget.TotalTime, systemTarget.TotalTime);
    CurrentSimulationPose.Kinematics = _program.RobotSystem.Kinematics(kineTargets, prevJoints);
    CurrentSimulationPose.TargetIndex = systemTarget.Index;
    CurrentSimulationPose.CurrentTime = time;
}
```

## How It All Works Together

1. The `Simulation` component renders a "Playback" button using the `ComponentButton` class.
2. When the user clicks the button, `ToggleForm()` is called, which creates and shows the `SimulationForm`.
3. The user interacts with the form:
   - Clicking Play toggles the playback state by calling `TogglePlay()`
   - Clicking Stop resets the simulation by calling `Stop()`
   - Adjusting the slider changes the playback speed by setting the `Speed` property
4. During playback, the `Update()` method increments the time based on real elapsed time and the speed setting.
5. Each update causes the component to re-solve, which calls `Animate()` on the program object.
6. The `Animate()` method calls `Step()` on the simulation object, which interpolates between keyframes.
7. The interpolated kinematics are then used to position the robot meshes for visualization.

## How to Apply It

To use the playback functionality in your own Grasshopper components:

1. Create a component that inherits from `GH_Component`.
2. Override the `CreateAttributes()` method to use a `ComponentButton`.
3. Implement methods for controlling time-based animation (toggle play, stop, etc.).
4. Create a form class that inherits from `ComponentForm` for user controls.
5. Connect the form's events to the component's control methods.

Example of adding a basic playback button to a component:

```csharp
public class MyAnimatedComponent : GH_Component
{
    private DateTime? _lastTime;
    private double _time = 0;
    
    // Constructor and other methods...
    
    public override void CreateAttributes()
    {
        m_attributes = new ComponentButton(this, "Animate", ToggleAnimation);
    }
    
    void ToggleAnimation()
    {
        if (_lastTime == null)
        {
            _lastTime = DateTime.Now;
            ExpireSolution(true);
        }
        else
        {
            _lastTime = null;
        }
    }
    
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Update time if animation is active
        if (_lastTime != null)
        {
            var now = DateTime.Now;
            TimeSpan delta = now - _lastTime.Value;
            _lastTime = now;
            _time += delta.TotalSeconds;
            
            // Schedule next update
            ExpireSolution(true);
        }
        
        // Use _time to update outputs
        // ...
    }
}
```

## Conclusion

The playback implementation in the Robots Grasshopper plugin provides a clean and effective way to control time-based animations. By separating the UI from the animation logic and using a custom component attribute for the button, it achieves a seamless integration with the Grasshopper interface while maintaining good separation of concerns.

The same pattern can be applied to any time-based simulation or animation in Grasshopper components, making it a useful reference for implementing similar functionality in other plugins.
