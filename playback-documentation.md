# Playback Button in the Program Simulation Component

## Overview

The Playback button feature in the Robots plugin for Grasshopper provides an interactive way to control robot simulation animations. This document outlines its implementation, functionality, and how to use it effectively.

## Implementation Details

The implementation spans multiple files but is primarily centered in these key files:

1. `Simulation.cs` - The main component class
2. `SimulationForm.cs` - The UI form that contains the playback controls
3. `ComponentButton.cs` - A custom button implementation for Grasshopper components

### Core Classes

#### 1. `Simulation` Class

Located in `src/Robots.Grasshopper/Program/Simulation.cs`, this class is a Grasshopper component that simulates a robot program. It manages the simulation state, handles component input/output, and defines the interactions with the playback UI.

Key properties and fields:
- `_form`: Reference to the playback UI dialog
- `_lastTime`: Nullable DateTime to track when the simulation last updated
- `_time`: Current simulation time in seconds
- `Speed`: Animation playback speed multiplier

Key methods:
- `TogglePlay()`: Start or pause the simulation playback
- `Stop()`: Reset the simulation to its initial state
- `Pause()`: Pause the simulation without resetting
- `Update()`: Advance the simulation based on elapsed time
- `ToggleForm()`: Show or hide the playback control form
- `SolveInstance()`: Core GH component method that updates the simulation state

#### 2. `SimulationForm` Class

Located in `src/Robots.Grasshopper/Program/SimulationForm.cs`, this class defines the UI dialog with playback controls.

Key elements:
- `Play`: CheckBox functioning as a toggle button for play/pause
- `stop`: Button to reset the simulation
- `slider`: Vertical slider to control playback speed
- Constructor that initializes the UI and attaches event handlers

#### 3. `ComponentButton` Class

Located in `src/Robots.Grasshopper/ComponentButton.cs`, this is a custom GH component attribute that adds a button to the component UI.

Key elements:
- Custom rendering of a button beneath the component
- Event handling for mouse interactions
- Action callback mechanism to connect UI interaction to component logic

## How the Playback Button Works

### Button Creation

1. The `Simulation` component overrides the `CreateAttributes()` method to replace the standard component attributes with `ComponentButton` attributes
2. The `ComponentButton` constructor takes a reference to the component, a label ("Playback"), and an action callback (`ToggleForm`)

```csharp
public override void CreateAttributes()
{
    m_attributes = new ComponentButton(this, "Playback", ToggleForm);
}
```

### Form Display

When the button is clicked, the `ToggleForm()` method is called, which:
1. Creates the `SimulationForm` if it doesn't exist yet
2. Toggles its visibility
3. Stops the simulation if the form is being hidden

```csharp
void ToggleForm()
{
    _form ??= new SimulationForm(this);
    _form.Visible = !_form.Visible;

    if (!_form.Visible)
        Stop();
}
```

### Play/Pause Functionality

The Play checkbox in the form calls the `TogglePlay()` method in the `Simulation` class:

```csharp
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
```

This method:
1. Starts the simulation by setting `_lastTime` to the current time if playback is not active
2. Calls `Pause()` if playback is already active
3. Triggers a component update via `ExpireSolution(true)`

### Time Update Mechanism

When playback is active:
1. The `Update()` method calculates time elapsed since the last frame
2. Advances the simulation time: `_time += delta.TotalSeconds * Speed`
3. Triggers another component update to refresh the display

### Speed Control

The speed slider adjusts the `Speed` property of the Simulation component, determining how quickly the simulation advances:

```csharp
slider.ValueChanged += (s, e) => component.Speed = (double)slider.Value / 100.0;
```

## How to Use the Playback Button

1. **Add the Simulation Component**: Find "Program simulation" (or "Sim") in the "Robots" > "Components" tab in Grasshopper.

2. **Connect Required Inputs**:
   - `P` (Program): Connect to the output of a Create Program component
   - `T` (Time): Optional time input (default: 0)
   - `N` (Normalized): Optional boolean to specify if time is normalized (default: true)

3. **View Outputs**:
   - `M` (System meshes): 3D geometry of robot at current time
   - `J` (Joint rotations): Current joint values
   - `P` (Plane): TCP position
   - `I` (Index): Current target index
   - `T` (Time): Current time in seconds
   - `P` (Program): Pass-through of the input program
   - `E` (Errors): Any simulation errors

4. **Interact with the Playback Button**:
   - Click the "Playback" button below the component to open the playback controls
   - Use the play/pause toggle (▶) to start/stop the animation
   - Use the stop button (■) to reset the animation to the beginning
   - Adjust the slider to control playback speed (100% is normal speed)

5. **Visualization**:
   - Connect the `M` output to a Display component to visualize the robot
   - Connect the component to a Custom Preview component for more control
   - Use the Simple Trail component to create a path visualization of the robot's motion

## Implementation Flow Diagram

```
User clicks "Playback" button
    │
    ▼
ToggleForm() is called
    │
    ▼
SimulationForm appears
    │
    ▼
User clicks Play button
    │
    ▼
TogglePlay() is called
    │
    ▼
_lastTime is set to current time
    │
    ▼
ExpireSolution(true) triggers component update
    │
    ▼
SolveInstance() runs
    │
    ▼
Update() calculates time delta
    │
    ▼
_time is updated based on Speed setting
    │
    ▼
Component outputs updated robot state
    │
    ▼
ExpireSolution(true) triggers next frame
```

## Advanced Usage

### Connecting to Other Components

The Simulation component is designed to work seamlessly with other Robots components:

- Connect its output to the **Simple Trail** component to visualize the robot's movement path
- Use the `Program` output to synchronize other visualization components with the simulation
- Connect the `Joint rotations` output to analysis components for motion studies

### Manual Time Control

Instead of using the Playback button, you can also:
- Manually input time values to the `T` input
- Connect a Number Slider to the `T` input for interactive scrubbing
- Connect a Grasshopper Timer component for automated animation

### Customizing the Simulation

The simulation can be controlled via GHPython or C# scripts by:
- Accessing the component's `_time` field
- Setting the `Speed` property
- Calling the `TogglePlay()`, `Stop()`, or `Pause()` methods

## Troubleshooting

- **Animation not playing**: Ensure the program has valid targets and no simulation errors
- **Slow performance**: Reduce the complexity of the robot meshes or increase the step size
- **Playback form not appearing**: Check if another instance is already open
- **Jerky animation**: Try adjusting the Speed slider to a lower value

## Conclusion

The Playback button is a powerful feature for interactively visualizing robot programs in Grasshopper. Its implementation balances functionality with user experience, making robot simulation accessible and intuitive.