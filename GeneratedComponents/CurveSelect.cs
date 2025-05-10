using Grasshopper;
public class CurveSelect : Component {
    public override void Initialize() {
        // Set the initial position of the curve.  This is a good place to set a default.
        this.SetPosition(0, 0);
    }
    public override void Update(int state, int t) {
        // Set the curve's position based on the slider value.
        if (state == 0) {
            this.SetPosition(0, 0);
        } else {
            this.SetPosition(0, 0);
        }
    }
}