// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;
using UnityEngine.InputSystem;

namespace DreamForge.Player
{
    /// <summary>
    /// Free first-person movement for walking a level: WASD or arrow keys, Shift to run,
    /// mouse look while the cursor is locked (click to lock, Esc to release). Gravity, no jumping.
    /// Uses the Input System directly; the project has the old Input Manager turned off.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [SerializeField] float moveSpeed = 4f;
        [SerializeField] float runMultiplier = 2f;
        [Tooltip("Degrees per pixel of mouse movement.")]
        [SerializeField] float lookSensitivity = 0.12f;
        [SerializeField] float gravity = -20f;

        public Camera Eye { get; private set; }
        /// <summary>When false, input is ignored (for example while the top-down camera is showing).</summary>
        public bool InputEnabled { get; set; } = true;

        CharacterController cc;
        float pitch, verticalSpeed;

        /// <summary>Sizes the capsule and puts the eye camera at <paramref name="eyeHeight"/> above the feet.</summary>
        public void Configure(float height, float radius, float eyeHeight)
        {
            cc = GetComponent<CharacterController>();
            cc.height = height;
            cc.radius = radius;
            cc.center = new Vector3(0, height / 2f, 0);   // transform sits at the feet
            cc.stepOffset = Mathf.Min(0.3f, height * 0.25f);
            cc.skinWidth = Mathf.Max(0.01f, radius * 0.1f);

            if (Eye == null)
            {
                var go = new GameObject("Eye");
                go.transform.SetParent(transform, false);
                Eye = go.AddComponent<Camera>();
                Eye.tag = "MainCamera";
                Eye.nearClipPlane = 0.05f;
                go.AddComponent<AudioListener>();
            }
            Eye.transform.localPosition = new Vector3(0, eyeHeight, 0);
        }

        /// <summary>Moves the feet to <paramref name="feet"/> without the controller resisting the jump.</summary>
        public void Teleport(Vector3 feet, float yawDegrees)
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0, yawDegrees, 0));
            cc.enabled = true;
            verticalSpeed = 0;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (InputEnabled && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (InputEnabled && Cursor.lockState == CursorLockMode.Locked && mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
                transform.Rotate(0, d.x, 0, Space.World);
                pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
                if (Eye != null) Eye.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }

            Vector2 input = Vector2.zero;
            if (InputEnabled && kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1;
            }
            Vector3 move = transform.right * input.x + transform.forward * input.y;
            if (move.sqrMagnitude > 1) move.Normalize();
            float speed = moveSpeed * (kb != null && kb.leftShiftKey.isPressed ? runMultiplier : 1f);

            verticalSpeed = cc.isGrounded ? -1f : verticalSpeed + gravity * Time.deltaTime;
            cc.Move((move * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
        }
    }
}
