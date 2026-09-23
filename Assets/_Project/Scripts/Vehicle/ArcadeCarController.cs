using UnityEngine;

/// One frame of driving intent. A human and an AI both produce one of these,
/// so the physics below never knows which is driving. This is the whole trick:
/// tune the car once, and every CPU opponent inherits the tuning for free.
public struct CarInputState
{
    public float throttle;   // -1 reverse .. +1 forward
    public float steer;      // -1 left .. +1 right
    public bool  handbrake;
    public bool  boost;
}

/// Raycast-suspension arcade car. Deliberately NOT WheelCollider: this is fewer
/// moving parts, far easier to tune toward a Rocket League feel, and it is
/// unambiguously our own implementation for the rubric.
///
/// Setup: car root holds the Rigidbody + this script. Add four empty child
/// transforms at the wheel corners (front two first) with zero local rotation,
/// and assign them to wheelPoints. Optionally assign visible wheel meshes.
///
/// NOTE: on Unity 6, rb.velocity is renamed rb.linearVelocity. If you see a
/// deprecation warning, that rename is the only change needed.
[RequireComponent(typeof(Rigidbody))]
public class ArcadeCarController : MonoBehaviour
{
    [Header("Wheels (front two first)")]
    public Transform[] wheelPoints = new Transform[4];
    public Transform[] wheelMeshes = new Transform[4];
    public int steeredWheelCount = 2;
    public int drivenWheelCount  = 4;
    public LayerMask groundMask = ~0;

    [Header("Suspension")]
    public float restLength      = 0.5f;
    public float springTravel    = 0.25f;
    public float wheelRadius     = 0.35f;
    public float springStrength  = 30000f;
    public float damperStrength  = 3000f;

    [Header("Drive")]
    public float enginePower       = 6000f;
    public float topSpeed          = 35f;
    public float reverseBrakeForce = 8000f;
    public float rollingResistance = 400f;
    public float maxSteerAngle     = 28f;
    public float steerResponse     = 10f;

    [Header("Grip — 0 is ice, 1 kills all sideways slide instantly")]
    [Range(0f, 1f)] public float frontGrip = 0.90f;
    [Range(0f, 1f)] public float rearGrip  = 0.75f;
    [Range(0f, 1f)] public float handbrakeRearGrip = 0.25f;
    public float tireMass  = 40f;
    public float downforce = 40f;

    [Header("Air control")]
    public float airRighting = 6f;
    public float airDamping  = 1.5f;

    [Header("Boost")]
    public float boostForce         = 9000f;
    public float boostCapacity      = 100f;
    public float boostDrainPerSec   = 40f;
    public float boostRefillPerSec  = 8f;

    /// Set this every frame from PlayerCarInput or AICarInput.
    public CarInputState Intent;

    public bool  IsGrounded { get; private set; }
    public float Boost      { get; private set; }
    public float ForwardSpeed => Vector3.Dot(transform.forward, rb.linearVelocity);

    Rigidbody rb;
    float currentSteerAngle;
    int   groundedWheels;
    float[] wheelSpin;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Low centre of mass — without this the car rolls over constantly.
        rb.centerOfMass += Vector3.down * 0.4f;
        wheelSpin = new float[wheelPoints.Length];
        Boost = boostCapacity;
    }

    void FixedUpdate()
    {
        currentSteerAngle = Mathf.Lerp(currentSteerAngle,
                                       Intent.steer * maxSteerAngle,
                                       steerResponse * Time.fixedDeltaTime);

        groundedWheels = 0;
        for (int i = 0; i < wheelPoints.Length; i++) UpdateWheel(i);
        IsGrounded = groundedWheels > 0;

        if (IsGrounded) rb.AddForce(-transform.up * downforce * rb.linearVelocity.magnitude);
        else            ApplyAirRighting();

        UpdateBoost();
    }

    void UpdateWheel(int i)
    {
        Transform wheel = wheelPoints[i];
        if (!wheel) return;

        bool steered = i < steeredWheelCount;
        bool driven  = i < drivenWheelCount;

        if (steered) wheel.localRotation = Quaternion.Euler(0f, currentSteerAngle, 0f);

        float maxDistance = restLength + springTravel + wheelRadius;
        if (!Physics.Raycast(wheel.position, -wheel.up, out RaycastHit hit, maxDistance, groundMask))
        {
            if (wheelMeshes[i]) wheelMeshes[i].position = wheel.position - wheel.up * (restLength + springTravel);
            return;
        }
        groundedWheels++;

        Vector3 wheelVel = rb.GetPointVelocity(wheel.position);

        // Suspension: spring pushes the chassis up, damper stops it pogoing.
        float compression = maxDistance - hit.distance;
        float springVel   = Vector3.Dot(wheel.up, wheelVel);
        rb.AddForceAtPosition(wheel.up * (compression * springStrength - springVel * damperStrength),
                              wheel.position);

        // Grip: cancel a fraction of sideways velocity each physics step.
        // This single number is what makes the car feel planted vs drifty.
        float grip = steered ? frontGrip : rearGrip;
        if (Intent.handbrake && !steered) grip = handbrakeRearGrip;
        float sidewaysVel   = Vector3.Dot(wheel.right, wheelVel);
        float desiredAccel  = -sidewaysVel * grip / Time.fixedDeltaTime;
        rb.AddForceAtPosition(wheel.right * (desiredAccel * tireMass), wheel.position);

        // Drive / brake
        if (driven)
        {
            float fwd = ForwardSpeed;
            if (Mathf.Abs(Intent.throttle) > 0.05f)
            {
                bool braking = Intent.throttle * fwd < 0f;
                float force  = braking ? reverseBrakeForce
                                       : enginePower * Mathf.Clamp01(1f - Mathf.Abs(fwd) / topSpeed);
                rb.AddForceAtPosition(wheel.forward * (Intent.throttle * force / drivenWheelCount),
                                      wheel.position);
            }
            else
            {
                float roll = Vector3.Dot(wheel.forward, wheelVel);
                rb.AddForceAtPosition(-wheel.forward * (roll * rollingResistance / drivenWheelCount),
                                      wheel.position);
            }
        }

        if (wheelMeshes[i])
        {
            wheelSpin[i] += Vector3.Dot(wheel.forward, wheelVel) / wheelRadius * Mathf.Rad2Deg * Time.fixedDeltaTime;
            wheelMeshes[i].position = hit.point + wheel.up * wheelRadius;
            wheelMeshes[i].rotation = wheel.rotation * Quaternion.Euler(wheelSpin[i], 0f, 0f);
        }
    }

    /// Nudges the car level while airborne so ramp landings aren't a coin flip.
    void ApplyAirRighting()
    {
        Vector3 righting = Vector3.Cross(transform.up, Vector3.up) * airRighting;
        rb.AddTorque(righting - rb.angularVelocity * airDamping, ForceMode.Acceleration);
    }

    void UpdateBoost()
    {
        if (Intent.boost && Boost > 0f)
        {
            rb.AddForce(transform.forward * boostForce);
            Boost = Mathf.Max(0f, Boost - boostDrainPerSec * Time.fixedDeltaTime);
        }
        else
        {
            Boost = Mathf.Min(boostCapacity, Boost + boostRefillPerSec * Time.fixedDeltaTime);
        }
    }

    /// Called by boost pads in the arena.
    public void AddBoost(float amount) => Boost = Mathf.Min(boostCapacity, Boost + amount);

    /// Called by the tag/shove ability so both players and AI get knocked around.
    public void ApplyShove(Vector3 force, Vector3 atPoint) =>
        rb.AddForceAtPosition(force, atPoint, ForceMode.Impulse);
}
