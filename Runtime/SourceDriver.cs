using UnityEngine;

public class R2ETSourceDriver : MonoBehaviour
{
    public SourceQuatReaderAuto targetChar;
    public Transform[] sourceJoints => targetChar.sourceJoints;           // 소스 캐릭터의 22개 관절
    public R2ETRetargetSentis retargeter;      // R2ET_Retargeter 오브젝트
    public float[] shapeAData;                 // (J*3) 길이, 캐릭터 shape 고정 값

    float[] seqABuf;
    float[] quatABuf;
    float[] skelABuf;
    float heightABuf;

    public SkinnedMeshRenderer smr;
    public int[] boneToJointIndex;

    int jointCount = 22;

    // root 속도 계산용
    Vector3 prevRootPos;
    bool hasPrevRootPos = false;

    void Start()
    {
        int J = jointCount;

        seqABuf = new float[J * 3 + 4];  // [dummy(3J), root_vel(3), root_rot_y(1)]
        quatABuf = new float[J * 4];
        skelABuf = new float[J * 3];

        boneToJointIndex = AutoBuildBoneToJointIndex(smr, targetChar.jointNames);
        shapeAData = ShapeExtractor.ComputeShapeVector(smr, jointCount, boneToJointIndex);
    }

    void LateUpdate()
    {
        FillFromSourceSkeleton();

        heightABuf = GetHeightFromSkel(skelABuf, jointCount);
       
        retargeter.RetargetOneFrame(seqABuf, quatABuf, skelABuf, shapeAData, heightABuf);
    }


    void FillFromSourceSkeleton()
    {
        int J = jointCount;

        // 1) 각 관절의 localRotation / localPosition → quatABuf, skelABuf
        for (int j = 0; j < J; j++)
        {
            Transform t = sourceJoints[j];

            // (1) localRotation → quatABuf : [w, x, y, z] 순서로 저장
            Quaternion q = t.localRotation;
            int qo = j * 4;
            quatABuf[qo + 0] = q.w;  // w
            quatABuf[qo + 1] = q.x;  // x
            quatABuf[qo + 2] = q.y;  // y
            quatABuf[qo + 3] = q.z;  // z

            // (2) localPosition → skelABuf : [x, y, z]
            Vector3 lp = t.localPosition;
            int so = j * 3;
            skelABuf[so + 0] = lp.x * 100;
            skelABuf[so + 1] = lp.y * 100;
            skelABuf[so + 2] = lp.z * 100;
        }

        // 2) seqAData 채우기
        //    - 앞의 3*J 는 사용 안 하는 dummy → 0으로 채움
        int dummyLen = J * 3;
        for (int i = 0; i < dummyLen; i++)
            seqABuf[i] = 0f;

        //    - root (여기서는 sourceJoints[0]을 root로 가정) 의 월드 위치와 yaw 사용
        Transform root = sourceJoints[0];
        Vector3 rootPos = root.position;              // 월드 기준 위치
        float yawDeg = root.eulerAngles.y;         // 월드 기준 yaw (deg)
        float yawRad = yawDeg * Mathf.Deg2Rad;     // 라디안으로 변환 (필요 없으면 deg 그대로 써도 됨)

        //    - root 속도 계산 (프레임 간 위치 차이 / deltaTime)
        Vector3 rootVel = Vector3.zero;
        if (hasPrevRootPos)
        {
            float dt = Time.deltaTime;
            if (dt > 1e-6f)
                rootVel = (rootPos - prevRootPos) / dt;
        }
        else
        {
            // 첫 프레임은 이전 위치가 없으므로 속도 0으로 두고, 플래그 세팅
            hasPrevRootPos = true;
        }
        prevRootPos = rootPos;

        //    - seqABuf 의 마지막 4차원 채우기: [root_vel_x, root_vel_y, root_vel_z, root_rot_y]
        int baseIdx = dummyLen;
        seqABuf[baseIdx + 0] = rootVel.x * 100;
        seqABuf[baseIdx + 1] = rootVel.y * 100;
        seqABuf[baseIdx + 2] = rootVel.z * 100;
        seqABuf[baseIdx + 3] = yawRad;   // or yawDeg (학습 때 쓴 단위에 맞춰야 함)
    }

    // skel: 길이 = J * 3, 각 joint마다 (x,y,z) 3개씩
    float GetHeightFromSkel(float[] skel, int jointCount)
    {
        // 1) 각 joint 벡터의 길이 = sqrt(x^2 + y^2 + z^2)
        float[] diffs = new float[jointCount];

        for (int j = 0; j < jointCount; j++)
        {
            int idx = j * 3;
            float x = skel[idx + 0];
            float y = skel[idx + 1];
            float z = skel[idx + 2];

            diffs[j] = Mathf.Sqrt(x * x + y * y + z * z);
        }

        // 2) 파이썬의 [1:6] + [7:10] 합치기
        //    → 1~5, 7~9 인덱스
        float height = 0f;

        for (int i = 1; i <= 5; i++)
            height += diffs[i];

        for (int i = 7; i <= 9; i++)
            height += diffs[i];

        // 3) /100 해서 최종 height
        return height;
    }

    int[] AutoBuildBoneToJointIndex(SkinnedMeshRenderer smr, string[] jointNames)
    {
        Transform[] bones = smr.bones;
        int boneCount = bones.Length;
        int jointCountLocal = jointNames.Length;
        
        int[] mapping = new int[boneCount];
        for (int i = 0; i < boneCount; i++)
            mapping[i] = -1; // 기본은 -1 (사용 안함)

        for (int b = 0; b < boneCount; b++)
        {
            string boneName = bones[b].name.ToLowerInvariant();
            int bestJoint = -1;
            int bestScore = 0;
            // Debug.Log(boneName);
            for (int j = 0; j < jointCountLocal; j++)
            {
                string jointName = jointNames[j].ToLowerInvariant();
                // boneName 에 jointName 이 포함되어 있으면 매칭 후보
                if (boneName.Contains(jointName))
                {
                    // 더 긴 jointName 일수록 더 구체적이니까 점수 높게
                    int score = jointName.Length;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestJoint = j;
                    }
                }
            }

            mapping[b] = bestJoint;
            // Debug.Log($"[AutoMap] bone {b}: {bones[b].name} -> jointIndex {bestJoint}");
        }

        return mapping;
    }

}
