using System.Collections.Generic;
using UnityEngine;
using Unity.Sentis;

public class R2ETRetargetSentis : MonoBehaviour
{
    [Header("Sentis model")]
    public ModelAsset modelAsset;                  // ONNX 임포트한 에셋
    public BackendType backend = BackendType.GPUCompute;

    [Header("Target skeleton")]
    public SourceQuatReaderAuto targetChar;
    public Transform[] targetJoints => targetChar.sourceJoints;               // J개의 타겟 관절
    public int jointCount = 22;                    // 우리 모델의 J

    private Model model;
    private Worker worker;

    void Awake()
    {
        model = ModelLoader.Load(modelAsset);
        worker = new Worker(model, backend);
    }

    void OnDestroy()
    {
        worker?.Dispose();
    }

    /// <summary>
    /// 한 프레임 단위 리타게팅.
    /// seqAData  : 길이 = J*3 + 4
    /// quatAData : 길이 = J*4
    /// skelAData : 길이 = J*3
    /// shapeAData: 길이 = J*3 (캐릭터 고정 값)
    /// </summary>
    public void RetargetOneFrame(
        float[] seqAData,
        float[] quatAData,
        float[] skelAData,
        float[] shapeAData,
        float heightAData
    )
    {
        int J = jointCount;
        // 1) TensorShape 설정 (N,H,W,C 같은 느낌으로만 맞추면 됨)
        var seqShape = new TensorShape(1, 1, J * 3 + 4); // (1,1,1,feat)
        var quatShape = new TensorShape(1, 1, J, 4);         // (1,1,J,4)
        var skelShape = new TensorShape(1, 1, J, 3);     // (1,1,J*3)
        var shapeShape = new TensorShape(1, J * 3);     // (1,J*3)
        var heightShape = new TensorShape(1, 1);     // (1,1)

        // 2) Tensor 생성 (float[] 데이터 그대로 사용)
        Tensor<float> seqA = new Tensor<float>(seqShape, seqAData);
        Tensor<float> quatA = new Tensor<float>(quatShape, quatAData);
        Tensor<float> skelA = new Tensor<float>(skelShape, skelAData);
        Tensor<float> shapeA = new Tensor<float>(shapeShape, shapeAData);
        Tensor<float> heightA = new Tensor<float>(heightShape, new float[] { heightAData });

        // 3) 입력 이름은 torch.onnx.export 때 지정한 이름과 일치해야 함
        worker.SetInput("seqA", seqA);
        worker.SetInput("quatA", quatA);
        worker.SetInput("skelA", skelA);    
        worker.SetInput("shapeA", shapeA);
        worker.SetInput("inp_height", heightA);
        //Debug.Log($"{quatAData[0]},{quatAData[1]},{quatAData[2]},{quatAData[3]},");
        //Debug.Log($"{quatA[0, 0, 0, 0]},{quatA[0, 0, 0, 1]},{quatA[0, 0, 0, 2]},{quatA[0, 0, 0, 3]},");
        //Debug.Log($"{skelAData[0]},{skelAData[1]},{skelAData[2]},");
        //Debug.Log($"{skelA[0, 0, 0, 0]},{skelA[0, 0, 0, 1]},{skelA[0, 0, 0, 2]},");

        worker.Schedule();

        // 4) 출력 받기 (이름도 export 때 지정한 그대로)
        // 1) 출력 텐서 가져오기
        Tensor<float> globalB = worker.PeekOutput("globalB") as Tensor<float>;
        Tensor<float> quatB = worker.PeekOutput("quatB") as Tensor<float>;

        // 2) GPU → CPU 보장 (필요시)
        globalB = globalB.ReadbackAndClone();
        quatB = quatB.ReadbackAndClone();
        
        // 3) float[]로 꺼내기
        float[] globalBData = globalB.DownloadToArray();
        float[] quatBData = quatB.DownloadToArray();
        // 6) 텐서 메모리 해제
        seqA.Dispose();
        quatA.Dispose();
        skelA.Dispose();
        shapeA.Dispose();
        globalB.Dispose();
        quatB.Dispose();
        globalB.Dispose();
        quatB.Dispose();

        // 7) 결과 타겟 캐릭터에 적용
        ApplyToTargetSkeleton(quatBData);
    }

    void ApplyToTargetSkeleton(float[] quatBData)
    {
        int J = targetJoints.Length;

        for (int j = 0; j < J; j++)
        {
            int o = j * 4;

            // ONNX 출력: [w, x, y, z]
            float w = quatBData[o + 0];
            float x = quatBData[o + 1];
            float y = quatBData[o + 2];
            float z = quatBData[o + 3];
            
            //if (j == 10)
            //{
            //    Debug.Log($"{w},{x},{y},{z},");
            //}

            // 1) 정규화 (길이 맞추기)
            float len = Mathf.Sqrt(w * w + x * x + y * y + z * z);
            if (len < 1e-8f)
            {
                w = 1; x = y = z = 0;
            }
            else
            {
                float inv = 1.0f / len;
                w *= inv; x *= inv; y *= inv; z *= inv;
            }

            // 2) Unity는 (x,y,z,w)
            Quaternion q = new Quaternion(x, y, z, w);

            // (필요하면 여기서 축 반전 한 번 더 시도해 볼 수 있음)
            // 예: z축 뒤집기 테스트
            // q = new Quaternion(q.x, q.y, -q.z, q.w);

            targetJoints[j].localRotation = q;
        }
    }

}
