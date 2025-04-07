using System.IO;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class SphericalLidarSimulationTest : MonoBehaviour
{
    public ParaMenu paraMenu;           // 引用 ParaMenu 脚本
    public float scanRange = 15f;       // 激光雷达的扫描范围
    public int numRaysPerAxis = 30;     // 每轴上的光束数量
    public GameObject nodePrefab;       // 用于表示节点的小球Prefab
    public LayerMask scanableLayer;     // 用于标识“scan_able”物体的层
    public float rayInterval = 0.1f;    // 每束光线发射的时间间隔（秒）
    public Button skipButton;           // Skip 按钮
    public LineRenderer linePrefab;     // 用于可视化光束的LineRenderer

    private bool isPaused = false;      // 标记是否暂停
    private bool isScanning = false;    // 标记是否正在扫描
    private bool isScanComplete = false;// 标记扫描是否完成
    private int scanCount = 1;          // 记录扫描次数
    private Coroutine scanCoroutine;    // 保存当前的扫描协程
    private List<GameObject> scannedNodes = new List<GameObject>(); // 存储生成的节点
    private List<LineRenderer> activeBeams = new List<LineRenderer>(); // 保存当前可视化的光束

    // 添加扫描过程的参数
    private int currentPhiIndex = 0;
    private int currentThetaIndex = 0;

    private List<Vector3> pointCloudPositions = new List<Vector3>();
    private List<Color> pointCloudColors = new List<Color>();

    void Start()
    {
        // 监听 Skip 按钮的点击事件
        Directory.CreateDirectory("Assets/Output");
        skipButton.onClick.AddListener(SkipScan);       
    }

    void Update()
    {
        // 获取 currentState
        int currentState = StateManager.currentState;

        // 在开始扫描之前更新 scanRange 和 numRaysPerAxis 的值
        UpdateScanParameters();

        // 当状态为 001 时开始或继续扫描
        if (currentState == 0b001 && !isScanning && !isScanComplete)
        {
            isPaused = false;
            isScanning = true;
            scanCoroutine = StartCoroutine(GradualScan());
        }
        // 当状态为 011 时暂停扫描
        else if (currentState == 0b011 && isScanning && !isPaused)
        {
            isPaused = true;
            StopCoroutine(scanCoroutine);
        }
        // 当状态再次为 001 时继续扫描
        else if (currentState == 0b001 && isScanning && isPaused)
        {
            isPaused = false;
            scanCoroutine = StartCoroutine(GradualScan());
        }
        // 当状态为 100 时结束扫描并生成父对象
        else if (currentState == 0b100)
        {
            EndScan();
            StopAllCoroutines();
        }
    }

    // 更新扫描参数的数值，分别从 textRange 和 textRes 获取值
    void UpdateScanParameters()
    {
        if (paraMenu != null)
        {
            // 读取 textRange 和 textRes 的值并转换为 float 和 int
            if (float.TryParse(paraMenu.textRange.text, out float rangeValue))
            {
                scanRange = rangeValue;  // 更新 scanRange
            }

            if (int.TryParse(paraMenu.textRes.text, out int resValue))
            {
                numRaysPerAxis = resValue;  // 更新 numRaysPerAxis
            }
        }
    }

    // 逐步扫描协程
    IEnumerator GradualScan()
    {
        float phiStep = 180f / numRaysPerAxis;    // Y轴步长（垂直）
        float thetaStep = 360f / numRaysPerAxis;  // X或Z轴步长（水平）

        // 从上次暂停的位置继续
        for (int i = currentPhiIndex; i < numRaysPerAxis; i++)
        {
            float phi = i * phiStep - 90f;  // 垂直旋转（围绕 Y 轴）

            // 围绕 X 或 Z 轴从 180 到 -180 扫描
            for (int j = currentThetaIndex; j < numRaysPerAxis; j++)
            {
                float theta = 180f - j * thetaStep;  // 从 180 到 -180，围绕 X 或 Z 轴旋转
                Vector3 direction = SphericalToCartesian(phi, theta);

                Ray ray = new Ray(transform.position, direction);
                RaycastHit hit;

                // 可视化光束
                VisualizeRay(transform.position, direction);

                if (Physics.Raycast(ray, out hit, scanRange, scanableLayer))
                {
                    Renderer renderer = hit.collider.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        // 获取碰撞点的UV坐标
                        Texture2D texture = renderer.material.mainTexture as Texture2D;
                        if (texture != null)
                        {
                            Vector2 pixelUV = hit.textureCoord;
                            pixelUV.x *= texture.width;
                            pixelUV.y *= texture.height;
                            // 获取对应的颜色
                            Color pointColor = texture.GetPixel((int)pixelUV.x, (int)pixelUV.y);
                            // 调整 hit 点的位置，将 y 坐标降低 70
                            Vector3 adjustedHitPoint = hit.point;
                            adjustedHitPoint.y -= 70;
                            // 生成节点
                            CreateNode(adjustedHitPoint, pointColor);
                        }
                    }
                }

                // 保存当前扫描的索引，防止暂停后丢失
                currentPhiIndex = i;
                currentThetaIndex = j;

                // 如果当前状态是暂停，则退出协程
                if (isPaused) yield break;

                yield return new WaitForSeconds(rayInterval);
            }

            // 重置 Theta 索引
            currentThetaIndex = 0;
        }

        EndScan();
    }

    // 可视化光束
    void VisualizeRay(Vector3 origin, Vector3 direction)
    {
        LineRenderer beam = Instantiate(linePrefab);
        beam.SetPosition(0, origin);
        beam.SetPosition(1, origin + direction * scanRange);
        activeBeams.Add(beam);

        // 销毁光束以防止堆积
        Destroy(beam.gameObject, rayInterval + 0.1f);
    }

    // 立即完成所有光束发射
    void SkipScan()
    {
        if (!isScanning) return;

        StopAllCoroutines();  // 停止逐步扫描
        float phiStep = 180f / numRaysPerAxis;    // 垂直角度步长
        float thetaStep = 360f / numRaysPerAxis;  // 水平角度步长

        for (int i = currentPhiIndex; i < numRaysPerAxis; i++)
        {
            float phi = i * phiStep;
            for (int j = currentThetaIndex; j < numRaysPerAxis; j++)
            {
                float theta = j * thetaStep;
                Vector3 direction = SphericalToCartesian(phi, theta);

                Ray ray = new Ray(transform.position, direction);
                RaycastHit hit;

                VisualizeRay(transform.position, direction);

                if (Physics.Raycast(ray, out hit, scanRange, scanableLayer))
                {
                    Renderer renderer = hit.collider.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        // 获取碰撞的UV坐标
                        Texture2D texture = renderer.material.mainTexture as Texture2D;
                        if (texture != null)
                        {
                            Vector2 pixelUV = hit.textureCoord;
                            pixelUV.x *= texture.width;
                            pixelUV.y *= texture.height;
                            // 获取对应的颜色
                            Color pointColor = texture.GetPixel((int)pixelUV.x, (int)pixelUV.y);
                            // 调整 hit 点的位置，将 y 坐标降低 70
                            Vector3 adjustedHitPoint = hit.point;
                            adjustedHitPoint.y -= 70;
                            // 生成节点
                            CreateNode(adjustedHitPoint, pointColor);
                        }
                    }
                }
            }
            // 重置Theta索引
            currentThetaIndex = 0;
        }

        EndScan();
    }

    // 创建节点
    void CreateNode(Vector3 position, Color color)
    {
        GameObject node = Instantiate(nodePrefab, position, Quaternion.identity);
        node.transform.localScale = Vector3.one * 0.1f; // 设置节点大小
        node.isStatic = false;
        Renderer renderer = node.GetComponent<Renderer>();
        pointCloudPositions.Add(position);
        pointCloudColors.Add(color);
        if (renderer != null)
        {
            renderer.material.color = color; // 设置颜色
        }
        scannedNodes.Add(node);  // 保存生成的节点
    }

    // 结束扫描并生成父对象
    void EndScan()
    {
        isScanning = false;
        isScanComplete = true;

        // 重置扫描索引
        currentPhiIndex = 0;
        currentThetaIndex = 0;

        // 生成一个父对象
        GameObject parentObject = new GameObject("Scan_" + scanCount);
        scanCount++;

        // 将所有节点设置为父对象的子对象
        foreach (var node in scannedNodes)
        {
            node.transform.SetParent(parentObject.transform);
        }

        scannedNodes.Clear();  // 清空已扫描节点列表
        StateManager.currentState = 0b000;  // 将 currentState 复位为 000
        isScanComplete = false;
        WritePointCloudToPCD("Assets/Output/scan_" + scanCount + ".pcd");
        pointCloudPositions.Clear();
        pointCloudColors.Clear();
    }

    void WritePointCloudToPCD(string filePath)
    {
        using (StreamWriter writer = new StreamWriter(filePath))
        {
            int pointCount = pointCloudPositions.Count;

            // PCD header
            writer.WriteLine("# .PCD v0.7 - Point Cloud Data file format");
            writer.WriteLine("VERSION 0.7");
            writer.WriteLine("FIELDS x y z rgb");
            writer.WriteLine("SIZE 4 4 4 4");
            writer.WriteLine("TYPE F F F F");
            writer.WriteLine("COUNT 1 1 1 1");
            writer.WriteLine($"WIDTH {pointCount}");
            writer.WriteLine("HEIGHT 1");
            writer.WriteLine("VIEWPOINT 0 0 0 1 0 0 0");
            writer.WriteLine($"POINTS {pointCount}");
            writer.WriteLine("DATA ascii");

            // Point data
            for (int i = 0; i < pointCount; i++)
            {
                Vector3 pos = pointCloudPositions[i];
                Color color = pointCloudColors[i];
                int rgb = ((int)(color.r * 255) << 16) | ((int)(color.g * 255) << 8) | ((int)(color.b * 255));
                writer.WriteLine($"{pos.x.ToString(CultureInfo.InvariantCulture)} {pos.y.ToString(CultureInfo.InvariantCulture)} {pos.z.ToString(CultureInfo.InvariantCulture)} {rgb}");
            }
        }

        Debug.Log("PCD file written to: " + filePath);
    }

    // 将球面坐标转换为笛卡尔坐标
    Vector3 SphericalToCartesian(float phi, float theta)
    {
        float phiRad = Mathf.Deg2Rad * phi;
        float thetaRad = Mathf.Deg2Rad * theta;

        float x = Mathf.Sin(phiRad) * Mathf.Cos(thetaRad);
        float y = Mathf.Sin(phiRad) * Mathf.Sin(thetaRad);
        float z = Mathf.Cos(phiRad);

        return new Vector3(x, y, z);
    }
}
