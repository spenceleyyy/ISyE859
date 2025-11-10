using UnityEngine;

public class KeyboardRigMover : MonoBehaviour
{
    public float moveSpeed = 2f;
    public float rotateSpeed = 60f;

    private void Update()
    {
        float move = 0f;
        float strafe = 0f;

        // Forward / back
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            move += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            move -= 1f;

        // Left / right
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            strafe += 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            strafe -= 1f;

        Vector3 direction = transform.forward * move + transform.right * strafe;
        if (direction.sqrMagnitude > 0.0001f)
        {
            transform.position += direction.normalized * moveSpeed * Time.deltaTime;
        }

        // Rotate with Q/E
        if (Input.GetKey(KeyCode.Q))
            transform.Rotate(0f, -rotateSpeed * Time.deltaTime, 0f);
        if (Input.GetKey(KeyCode.E))
            transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f);
    }
}