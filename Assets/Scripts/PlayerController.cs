using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] float moveSpeed;
    [SerializeField] float pickupRange = 2.2f;
    [SerializeField] Vector3 holdLocalOffset = new Vector3(0.45f, 0.15f, 0.7f);
    [SerializeField] float throwSpeed = 8f;
    [SerializeField] float throwUpSpeed = 2.5f;
    [SerializeField] float dropForward = 0.9f;

    Rigidbody rb;
    Vector3 moveDir;
    Pickable held;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (ForgeSessionController.IsBlockingPlayer)
        {
            moveDir = Vector3.zero;
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        rb.linearVelocity = new Vector3(moveDir.x * moveSpeed, rb.linearVelocity.y, moveDir.z * moveSpeed);
    }

    void OnMove(InputValue _value)
    {
        if (ForgeSessionController.IsBlockingPlayer)
        {
            moveDir = Vector3.zero;
            return;
        }

        Vector3 camForward = Camera.main.transform.forward;
        camForward.y = 0f;
        camForward.Normalize();
        Vector3 camRight = Camera.main.transform.right;
        camRight.y = 0f;
        camRight.Normalize();
        Vector2 inputDir = _value.Get<Vector2>();
        moveDir = (camForward * inputDir.y + camRight * inputDir.x).normalized;
    }

    void OnInteract()
    {
        if (ForgeSessionController.IsBlockingPlayer)
            return;

        if (held != null)
        {
            Anvil anvil = Anvil.FindClosestInRange(transform.position, pickupRange);
            if (anvil != null && anvil.TryPlaceHeld(held))
            {
                held = null;
                return;
            }

            Vector3 facing = GetFacing();
            Vector3 dropPos = transform.position + facing * dropForward + Vector3.up * 0.35f;
            held.Drop(dropPos, rb.linearVelocity + facing * 1.5f);
            held = null;
            return;
        }

        Pickable nearest = FindClosestPickable();
        if (nearest != null)
        {
            nearest.PickUp(transform, holdLocalOffset);
            if (nearest.IsHeld)
                held = nearest;
        }
    }

    void OnAttack()
    {
        if (ForgeSessionController.IsBlockingPlayer)
            return;

        if (held == null)
            return;

        Vector3 facing = GetFacing();
        Vector3 throwPos = transform.position + facing * dropForward + Vector3.up * 0.55f;
        held.Throw(throwPos, rb.linearVelocity + facing * throwSpeed + Vector3.up * throwUpSpeed);
        
        //if (held.TryGetComponent(out HeatableMetal x))
        //    x.ClearForgeProgress();
        held = null;
    }

    Vector3 GetFacing()
    {
        if (moveDir.sqrMagnitude > 0.01f)
            return moveDir;

        Vector3 camForward = Camera.main.transform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude > 0.01f)
            return camForward.normalized;

        return Vector3.forward;
    }

    Pickable FindClosestPickable()
    {
        Pickable closest = null;
        float best = pickupRange * pickupRange;
        var pickables = FindObjectsByType<Pickable>(FindObjectsSortMode.None);
        Vector3 origin = transform.position;
        for (int i = 0; i < pickables.Length; i++)
        {
            Pickable pickable = pickables[i];
            if (pickable == null || !pickable.CanBePickedUp)
                continue;

            float distSq = (pickable.transform.position - origin).sqrMagnitude;
            if (distSq > best)
                continue;

            best = distSq;
            closest = pickable;
        }

        return closest;
    }
}
