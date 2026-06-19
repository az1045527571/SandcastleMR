using UnityEngine;

/// <summary>
/// 轨道相机：右键拖动旋转，滚轮缩放，中键平移。
/// 挂到 Camera 上，或者由 Bootstrap 自动添加。
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    [Header("目标与距离")]
    public Vector3 targetPoint = Vector3.zero;
    public float distance = 8f;
    public float minDistance = 2f;
    public float maxDistance = 30f;

    [Header("旋转")]
    public float rotSpeed = 4f;
    public float yMinAngle = 5f;
    public float yMaxAngle = 85f;

    [Header("平移")]
    public float panSpeed = 0.5f;

    [Header("缩放")]
    public float zoomSpeed = 2f;

    private float _yaw = 45f;
    private float _pitch = 30f;

    void LateUpdate()
    {
        // 右键旋转
        if (Input.GetMouseButton(1))
        {
            _yaw += Input.GetAxis("Mouse X") * rotSpeed;
            _pitch -= Input.GetAxis("Mouse Y") * rotSpeed;
            _pitch = Mathf.Clamp(_pitch, yMinAngle, yMaxAngle);
        }

        // 中键平移
        if (Input.GetMouseButton(2))
        {
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            targetPoint -= right * Input.GetAxis("Mouse X") * panSpeed;
            targetPoint -= up * Input.GetAxis("Mouse Y") * panSpeed;
        }

        // 滚轮缩放
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance -= scroll * zoomSpeed * distance;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        // 应用变换
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 offset = rot * new Vector3(0f, 0f, -distance);
        transform.position = targetPoint + offset;
        transform.rotation = rot;
    }
}
