using UnityEngine;
using UnityEngine.AI;

/// Proof that a CPU car drives through the exact same physics as the player.
///
/// The NavMeshAgent is used ONLY as a path solver — it never moves anything.
/// We read its computed corners and convert "next corner" into steering, so the
/// car is still a real rigid body: bumpable, boostable, able to fly off ramps.
/// Set the agent's Update Position / Update Rotation OFF in the inspector.
///
/// This is a placeholder for the AI workstream. Replace SetDestination's target
/// with whatever the FSM currently wants: the runner to chase, a flee point,
/// the nearest boost pad.
[RequireComponent(typeof(ArcadeCarController))]
public class AICarInput : MonoBehaviour
{
    public Transform target;
    public NavMeshAgent agent;

    [Tooltip("Degrees of heading error that equals full steering lock.")]
    public float steerSensitivity = 30f;
    [Tooltip("Ease off the throttle in hard corners so the car doesn't understeer into walls.")]
    public float corneringThrottleCut = 0.6f;
    public float repathInterval = 0.25f;

    ArcadeCarController car;
    float repathTimer;

    void Awake()
    {
        car = GetComponent<ArcadeCarController>();
        if (agent)
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
        }
    }

    void Update()
    {
        if (!agent || !target) return;

        // Keep the solver anchored to where physics actually put the car.
        agent.nextPosition = transform.position;

        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f)
        {
            agent.SetDestination(target.position);
            repathTimer = repathInterval;
        }

        Vector3 corner = NextCorner();
        Vector3 toCorner = corner - transform.position;
        toCorner.y = 0f;
        if (toCorner.sqrMagnitude < 0.01f) return;

        float headingError = Vector3.SignedAngle(transform.forward, toCorner, Vector3.up);
        float steer = Mathf.Clamp(headingError / steerSensitivity, -1f, 1f);

        car.Intent = new CarInputState
        {
            steer     = steer,
            throttle  = Mathf.Lerp(1f, corneringThrottleCut, Mathf.Abs(steer)),
            handbrake = false,
            boost     = Mathf.Abs(steer) < 0.2f && toCorner.magnitude > 15f,
        };
    }

    Vector3 NextCorner()
    {
        Vector3[] corners = agent.path.corners;
        // corners[0] is our own position; aim at the one after it.
        return corners.Length > 1 ? corners[1] : target.position;
    }
}
