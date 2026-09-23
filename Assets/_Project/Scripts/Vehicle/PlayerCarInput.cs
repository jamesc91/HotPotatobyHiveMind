using UnityEngine;

/// Reads the human and hands intent to the shared controller.
/// Uses the legacy Input axes so the prototype runs with zero setup — these
/// already read analog gamepad sticks. Swap to the Input System package when
/// you want analog triggers for throttle (worth doing before the final build;
/// the rubric explicitly rewards analog control).
[RequireComponent(typeof(ArcadeCarController))]
public class PlayerCarInput : MonoBehaviour
{
    ArcadeCarController car;

    void Awake() => car = GetComponent<ArcadeCarController>();

    void Update()
    {
        car.Intent = new CarInputState
        {
            throttle  = Input.GetAxis("Vertical"),
            steer     = Input.GetAxis("Horizontal"),
            handbrake = Input.GetButton("Jump"),    // Space / gamepad A
            boost     = Input.GetButton("Fire3"),   // Left Shift / gamepad LB
        };
    }
}
