using UnityEngine;

/// <summary>
/// jointNames 리스트에 적힌 이름대로
/// 리그 트리에서 Transform을 자동으로 찾아 sourceJoints를 채운 뒤,
/// 매 프레임 각 관절의 로컬 쿼터니언(quatA_t)을 읽어오는 스크립트.
/// </summary>
public class SourceQuatReaderAuto : MonoBehaviour
{
    public Transform character;
    public string[] jointNames;    // ex) 길이 22

    public Transform[] sourceJoints; // jointNames와 같은 길이

    // 현재 프레임의 로컬 쿼터니언 (Unity 형식)
    public Quaternion[] quatA;       // (N,)
    // ONNX 입력용 flatten (x,y,z,w 순서)
    public float[] quatAFlat;        // (N*4,)

    private void Awake()
    {
        jointNames = new string[] {
            "Hip",
            "Spine",
            "Spine1",
            "Spine2",
            "Neck",
            "Head",
            "LeftUpLeg",
            "LeftLeg",
            "LeftFoot",
            "LeftToeBase",
            "RightUpLeg",
            "RightLeg",
            "RightFoot",
            "RightToeBase",
            "LeftShoulder",
            "LeftArm",
            "LeftForeArm",
            "LeftHand",
            "RightShoulder",
            "RightArm",
            "RightForeArm",
            "RightHand", 
        }; 
        

        if (jointNames == null || jointNames.Length == 0)
        {
            Debug.LogError("[SourceQuatReaderAuto] jointNames를 설정해 주세요.");
            return;
        }

        sourceJoints = new Transform[jointNames.Length];
        quatA = new Quaternion[jointNames.Length];
        quatAFlat = new float[jointNames.Length * 4];

        // 1) 이름으로 Transform 자동 매핑
        for (int i = 0; i < jointNames.Length; i++)
        {
            string jName = jointNames[i];
            Transform found = FindDeepChild(character, jName);

            if (found == null)
            {
                Debug.LogError($"[SourceQuatReaderAuto] 자식 트리에서 '{jName}' 조인트를 찾지 못했습니다.");
            }
            else
            {
                sourceJoints[i] = found;
                // Debug.Log($"[SourceQuatReaderAuto] 매핑: jointNames[{i}] = {jName} → {found.name}");
            }
        }
    }

    /// <summary>
    /// 현재 GameObject를 루트로 하위 전체 트리에서 name이 같은 Transform을 찾는다.
    /// </summary>
    private Transform FindDeepChild(Transform parent, string name)
    {
        // 1) 자기 자신 검사
        if (parent.name.Contains(name))
            return parent;

        // 2) 자식들 재귀 탐색
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            Transform result = FindDeepChild(child, name);
            if (result != null)
                return result;
        }
        return null;
    }

    // Animator 업데이트가 끝난 뒤에 읽기 위해 LateUpdate 사용
    private void LateUpdate()
    {
        if (sourceJoints == null || sourceJoints.Length == 0) return;

        // 2) 각 관절 로컬 회전 읽기
        for (int i = 0; i < sourceJoints.Length; i++)
        {
            if (sourceJoints[i] == null)
            {
                // Awake에서 못 찾은 경우
                continue;
            }

            quatA[i] = sourceJoints[i].localRotation;
        }

        // 3) float 벡터로 펼치기 (x, y, z, w)
        int idx = 0;
        for (int i = 0; i < quatA.Length; i++)
        {
            Quaternion q = quatA[i];
            quatAFlat[idx++] = q.x;
            quatAFlat[idx++] = q.y;
            quatAFlat[idx++] = q.z;
            quatAFlat[idx++] = q.w;
        }

        // 디버그 예시: 첫 조인트 로그
        // if (quatA.Length > 0)
        //     Debug.Log($"[{Time.frameCount}] {jointNames[0]} quat = {quatA[0]}");
    }

    /// <summary>
    /// 외부에서 현재 프레임의 quatA를 float[]로 복사하고 싶을 때 사용.
    /// </summary>
    public void CopyQuatA(float[] targetBuffer)
    {
        if (targetBuffer == null || targetBuffer.Length < quatAFlat.Length)
        {
            Debug.LogError("[SourceQuatReaderAuto] targetBuffer 길이가 부족합니다.");
            return;
        }

        System.Array.Copy(quatAFlat, targetBuffer, quatAFlat.Length);
    }
}
