/// <summary>
/// Provides the PerspectiveProjection class for off-axis projection rendering.
///
/// Calculates custom projection matrices for VR displays based on physical screen
/// position relative to the camera, enabling accurate perspective for multi-monitor setups.
/// </summary>
using UnityEngine;
using UnityEngine.Serialization;

namespace Gimbl
{
    /// <summary>
    /// Handles off-axis perspective projection for VR displays.
    /// </summary>
    [ExecuteInEditMode]
    public class PerspectiveProjection : MonoBehaviour
    {
        /// <summary>The shader property name driving brightness adjustment in the post-processing material.</summary>
        private const string BrightnessShaderProperty = "_brightness";

        /// <summary>The GameObject representing the physical projection screen.</summary>
        public GameObject projectionScreen;

        /// <summary>The display object this projection belongs to.</summary>
        [FormerlySerializedAs("dispObj")]
        public Gimbl.DisplayObject displayObject;

        /// <summary>Determines whether to estimate view frustum for culling.</summary>
        public bool estimateViewFrustum = true;

        /// <summary>Determines whether to automatically set the near clip plane.</summary>
        public bool setNearClipPlane = true;

        /// <summary>The offset applied to the near clip plane distance.</summary>
        public float nearClipDistanceOffset = -0.01f;

        /// <summary>Determines whether debug logging is enabled.</summary>
        public bool isDebug = false;

        /// <summary>The material for brightness adjustment post-processing.</summary>
        public Material material;

        /// <summary>The mesh type of the projection screen (Plane or Quad).</summary>
        private string _meshType;

        /// <summary>The camera component for this projection.</summary>
        private Camera _cameraComponent;

        /// <summary>The projection matrix.</summary>
        private Matrix4x4 _projectionMatrix;

        /// <summary>The rotation matrix.</summary>
        private Matrix4x4 _rotationMatrix;

        /// <summary>The translation matrix.</summary>
        private Matrix4x4 _translationMatrix;

        /// <summary>The quaternion for camera rotation.</summary>
        private Quaternion _cameraRotation;

        /// <summary>
        /// Initializes the brightness shader material, display object reference, and camera component.
        /// </summary>
        private void Awake()
        {
            if (material == null)
            {
                material = new Material(Shader.Find("Hidden/BrightnessShader"));
            }
            displayObject = GetComponentInParent<Gimbl.DisplayObject>();
            _cameraComponent = GetComponent<Camera>();
        }

        /// <summary>Updates the projection view after all other updates.</summary>
        private void LateUpdate()
        {
            UpdateView();
        }

        /// <summary>Calculates and applies the off-axis projection matrix.</summary>
        public void UpdateView()
        {
            if (projectionScreen == null)
            {
                return;
            }
            if (_meshType == null)
            {
                _meshType = projectionScreen.GetComponent<MeshFilter>().sharedMesh.name;
            }
            if (_cameraComponent == null)
            {
                _cameraComponent = GetComponent<Camera>();
            }
            if (_cameraComponent == null)
            {
                return;
            }

            Vector3 screenLowerLeft = new Vector3();
            Vector3 screenLowerRight = new Vector3();
            Vector3 screenUpperLeft = new Vector3();
            switch (_meshType)
            {
                case "Plane":
                    screenLowerLeft = projectionScreen.transform.TransformPoint(new Vector3(-5.0f, 0.0f, -5.0f));
                    screenLowerRight = projectionScreen.transform.TransformPoint(new Vector3(5.0f, 0.0f, -5.0f));
                    screenUpperLeft = projectionScreen.transform.TransformPoint(new Vector3(-5.0f, 0.0f, 5.0f));
                    break;
                case "Quad":
                    screenLowerLeft = projectionScreen.transform.TransformPoint(new Vector3(-0.5f, -0.5f, 0.0f));
                    screenLowerRight = projectionScreen.transform.TransformPoint(new Vector3(0.5f, -0.5f, 0.0f));
                    screenUpperLeft = projectionScreen.transform.TransformPoint(new Vector3(-0.5f, 0.5f, 0.0f));
                    break;
            }

            Vector3 eyePosition = transform.position;
            float nearClipDistance = _cameraComponent.nearClipPlane;
            float farClipDistance = _cameraComponent.farClipPlane;

            Vector3 screenRightAxis = screenLowerRight - screenLowerLeft;
            Vector3 screenUpAxis = screenUpperLeft - screenLowerLeft;
            Vector3 eyeToLowerLeft = screenLowerLeft - eyePosition;
            Vector3 eyeToLowerRight = screenLowerRight - eyePosition;
            Vector3 eyeToUpperLeft = screenUpperLeft - eyePosition;

            if (Vector3.Dot(-Vector3.Cross(eyeToLowerLeft, eyeToUpperLeft), eyeToLowerRight) < 0.0)
            {
                if (isDebug)
                {
                    Debug.Log("Facing backface of plane");
                }
                screenUpAxis = -screenUpAxis;
                screenLowerLeft = screenUpperLeft;
                screenLowerRight = screenLowerLeft + screenRightAxis;
                screenUpperLeft = screenLowerLeft + screenUpAxis;
                eyeToLowerLeft = screenLowerLeft - eyePosition;
                eyeToLowerRight = screenLowerRight - eyePosition;
                eyeToUpperLeft = screenUpperLeft - eyePosition;
            }
            else
            {
                if (isDebug)
                {
                    Debug.Log("Not Facing backface of plane");
                }
            }

            screenRightAxis.Normalize();
            screenUpAxis.Normalize();
            Vector3 screenNormal = -Vector3.Cross(screenRightAxis, screenUpAxis);

            float eyeToScreenDistance = -Vector3.Dot(eyeToLowerLeft, screenNormal);
            if (setNearClipPlane)
            {
                nearClipDistance = eyeToScreenDistance + nearClipDistanceOffset;
                _cameraComponent.nearClipPlane = nearClipDistance;
            }
            float leftEdgeDistance =
                Vector3.Dot(screenRightAxis, eyeToLowerLeft) * nearClipDistance / eyeToScreenDistance;
            float rightEdgeDistance =
                Vector3.Dot(screenRightAxis, eyeToLowerRight) * nearClipDistance / eyeToScreenDistance;
            float bottomEdgeDistance =
                Vector3.Dot(screenUpAxis, eyeToLowerLeft) * nearClipDistance / eyeToScreenDistance;
            float topEdgeDistance = Vector3.Dot(screenUpAxis, eyeToUpperLeft) * nearClipDistance / eyeToScreenDistance;

            _projectionMatrix[0, 0] = 2.0f * nearClipDistance / (rightEdgeDistance - leftEdgeDistance);
            _projectionMatrix[0, 1] = 0.0f;
            _projectionMatrix[0, 2] = (rightEdgeDistance + leftEdgeDistance) / (rightEdgeDistance - leftEdgeDistance);
            _projectionMatrix[0, 3] = 0.0f;

            _projectionMatrix[1, 0] = 0.0f;
            _projectionMatrix[1, 1] = 2.0f * nearClipDistance / (topEdgeDistance - bottomEdgeDistance);
            _projectionMatrix[1, 2] = (topEdgeDistance + bottomEdgeDistance) / (topEdgeDistance - bottomEdgeDistance);
            _projectionMatrix[1, 3] = 0.0f;

            _projectionMatrix[2, 0] = 0.0f;
            _projectionMatrix[2, 1] = 0.0f;
            _projectionMatrix[2, 2] = (farClipDistance + nearClipDistance) / (nearClipDistance - farClipDistance);
            _projectionMatrix[2, 3] = 2.0f * farClipDistance * nearClipDistance / (nearClipDistance - farClipDistance);

            _projectionMatrix[3, 0] = 0.0f;
            _projectionMatrix[3, 1] = 0.0f;
            _projectionMatrix[3, 2] = -1.0f;
            _projectionMatrix[3, 3] = 0.0f;

            _rotationMatrix[0, 0] = screenRightAxis.x;
            _rotationMatrix[0, 1] = screenRightAxis.y;
            _rotationMatrix[0, 2] = screenRightAxis.z;
            _rotationMatrix[0, 3] = 0.0f;

            _rotationMatrix[1, 0] = screenUpAxis.x;
            _rotationMatrix[1, 1] = screenUpAxis.y;
            _rotationMatrix[1, 2] = screenUpAxis.z;
            _rotationMatrix[1, 3] = 0.0f;

            _rotationMatrix[2, 0] = screenNormal.x;
            _rotationMatrix[2, 1] = screenNormal.y;
            _rotationMatrix[2, 2] = screenNormal.z;
            _rotationMatrix[2, 3] = 0.0f;

            _rotationMatrix[3, 0] = 0.0f;
            _rotationMatrix[3, 1] = 0.0f;
            _rotationMatrix[3, 2] = 0.0f;
            _rotationMatrix[3, 3] = 1.0f;

            _translationMatrix[0, 0] = 1.0f;
            _translationMatrix[0, 1] = 0.0f;
            _translationMatrix[0, 2] = 0.0f;
            _translationMatrix[0, 3] = -eyePosition.x;

            _translationMatrix[1, 0] = 0.0f;
            _translationMatrix[1, 1] = 1.0f;
            _translationMatrix[1, 2] = 0.0f;
            _translationMatrix[1, 3] = -eyePosition.y;

            _translationMatrix[2, 0] = 0.0f;
            _translationMatrix[2, 1] = 0.0f;
            _translationMatrix[2, 2] = 1.0f;
            _translationMatrix[2, 3] = -eyePosition.z;

            _translationMatrix[3, 0] = 0.0f;
            _translationMatrix[3, 1] = 0.0f;
            _translationMatrix[3, 2] = 0.0f;
            _translationMatrix[3, 3] = 1.0f;

            _cameraComponent.projectionMatrix = _projectionMatrix;
            _cameraComponent.worldToCameraMatrix = _rotationMatrix * _translationMatrix;

            if (estimateViewFrustum)
            {
                _cameraRotation.SetLookRotation(
                    (0.5f * (screenLowerRight + screenUpperLeft) - eyePosition),
                    screenUpAxis
                );
                _cameraComponent.transform.rotation = _cameraRotation;

                if (_cameraComponent.aspect >= 1.0)
                {
                    _cameraComponent.fieldOfView =
                        Mathf.Rad2Deg
                        * Mathf.Atan(
                            (
                                (screenLowerRight - screenLowerLeft).magnitude
                                + (screenUpperLeft - screenLowerLeft).magnitude
                            ) / eyeToLowerLeft.magnitude
                        );
                }
                else
                {
                    _cameraComponent.fieldOfView =
                        Mathf.Rad2Deg
                        / _cameraComponent.aspect
                        * Mathf.Atan(
                            (
                                (screenLowerRight - screenLowerLeft).magnitude
                                + (screenUpperLeft - screenLowerLeft).magnitude
                            ) / eyeToLowerLeft.magnitude
                        );
                }
            }
        }

        /// <summary>Applies brightness adjustment to the rendered image.</summary>
        /// <param name="source">The source render texture.</param>
        /// <param name="destination">The destination render texture.</param>
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (displayObject == null)
            {
                material.SetFloat(BrightnessShaderProperty, 100f);
            }
            else
            {
                material.SetFloat(BrightnessShaderProperty, displayObject.currentBrightness);
            }
            Graphics.Blit(source, destination, material);
        }
    }
}
