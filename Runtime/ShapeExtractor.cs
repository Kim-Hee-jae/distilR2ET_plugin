using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Blender extract_shape.py 의 full_width, joint_shape, shape_vector 계산을
/// Unity SkinnedMeshRenderer + boneWeights 를 사용해 재구현한 헬퍼.
/// </summary>
public static class ShapeExtractor
{
    /// <param name="smr">
    ///     SkinnedMeshRenderer (rest pose 상태의 캐릭터, sharedMesh 사용)
    /// </param>
    /// <param name="jointCount">
    ///     모델에서 사용하는 simplified joint 개수 (예: 22)
    /// </param>
    /// <param name="boneToJointIndex">
    ///     길이 = smr.bones.Length 인 배열.
    ///     boneToJointIndex[b] = 0..jointCount-1 : 이 bone이 해당 joint에 매핑
    ///                            -1             : 사용하지 않음 / 무시
    /// </param>
    /// <returns>
    ///     길이 jointCount*3 짜리 1D shape 벡터.
    ///     Python 의 shape_vector (22,3).reshape(-1) 와 동일한 구조:
    ///     [ j0_x, j0_y, j0_z, j1_x, j1_y, j1_z, ... ]
    /// </returns>
    public static float[] ComputeShapeVector(SkinnedMeshRenderer smr, int jointCount, int[] boneToJointIndex)
    {
        if (smr == null)
        {
            Debug.LogError("ShapeExtractor.ComputeShapeVector: smr is null.");
            return null;
        }

        Mesh mesh = smr.sharedMesh;
        if (mesh == null)
        {
            Debug.LogError("ShapeExtractor.ComputeShapeVector: smr.sharedMesh is null.");
            return null;
        }

        if (boneToJointIndex == null || boneToJointIndex.Length != smr.bones.Length)
        {
            Debug.LogError("ShapeExtractor.ComputeShapeVector: boneToJointIndex length must equal smr.bones.Length.");
            return null;
        }

        Vector3[] vertices = mesh.vertices;          // (numVerts,)
        BoneWeight[] weights = mesh.boneWeights;     // (numVerts,)

        int vertCount = vertices.Length;
        int weightCount = weights.Length;
        int numVerts = Mathf.Min(vertCount, weightCount);

        if (numVerts == 0)
        {
            Debug.LogError("ShapeExtractor: mesh.boneWeights is empty. Skinned mesh가 맞는지, Import 설정(Skin Weights) 확인 필요.");
            return new float[jointCount * 3]; // 전부 0 반환
        }

        // 1) full_width = get_width(all vertices used)
        Vector3 min = vertices[0];
        Vector3 max = vertices[0];
        for (int i = 1; i < numVerts; i++)
        {
            Vector3 v = vertices[i];
            if (v.x < min.x) min.x = v.x;
            if (v.y < min.y) min.y = v.y;
            if (v.z < min.z) min.z = v.z;

            if (v.x > max.x) max.x = v.x;
            if (v.y > max.y) max.y = v.y;
            if (v.z > max.z) max.z = v.z;
        }
        Vector3 fullWidth = max - min;  // (x,y,z)

        // 2) vertex_part: 각 vertex를 jointCount 개 중 하나에 할당
        List<Vector3>[] jointVerts = new List<Vector3>[jointCount];
        for (int j = 0; j < jointCount; j++)
            jointVerts[j] = new List<Vector3>();

        int assignedVertCount = 0;

        for (int i = 0; i < numVerts; i++)
        {
            BoneWeight bw = weights[i];

            // bone index / weight 배열로 정리
            int[] bIdx = new int[4] { bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3 };
            float[] bW = new float[4] { bw.weight0, bw.weight1, bw.weight2, bw.weight3 };

            int chosenJoint = -1;
            float bestW = 0f;

            // 2-1) 유효한 joint 매핑 중에서 가장 weight 큰 joint 고르기
            for (int k = 0; k < 4; k++)
            {
                int boneIndex = bIdx[k];
                if (boneIndex < 0 || boneIndex >= boneToJointIndex.Length)
                    continue;

                int jIdx = boneToJointIndex[boneIndex];
                if (jIdx < 0 || jIdx >= jointCount)
                    continue;

                float w = bW[k];
                if (w > bestW)
                {
                    bestW = w;
                    chosenJoint = jIdx;
                }
            }

            // 2-2) 어떤 bone도 joint에 매핑되지 않았다면,
            //      최소한 root(0)에는 밀어넣어서 jointVerts가 비지 않게 한다.
            if (chosenJoint < 0)
            {
                chosenJoint = 0; // 필요하면 파라미터로 rootJointIndex 받아서 쓰도록 변경 가능
            }

            jointVerts[chosenJoint].Add(vertices[i]);
            assignedVertCount++;
        }

        if (assignedVertCount == 0)
        {
            Debug.LogError("ShapeExtractor: no vertices were assigned to any joint. boneToJointIndex 매핑을 다시 확인하세요.");
            return new float[jointCount * 3];
        }

        // 3) joint_shape: 각 joint j에 대해, 그 joint에 할당된 vertices 의 bbox width 계산
        float[] shapeVector = new float[jointCount * 3];

        const float eps = 1e-8f;
        float fwX = Mathf.Abs(fullWidth.x) < eps ? 1f : fullWidth.x;
        float fwY = Mathf.Abs(fullWidth.y) < eps ? 1f : fullWidth.y;
        float fwZ = Mathf.Abs(fullWidth.z) < eps ? 1f : fullWidth.z;

        for (int j = 0; j < jointCount; j++)
        {
            List<Vector3> vList = jointVerts[j];
            Vector3 jointWidth = Vector3.zero;

            if (vList.Count > 0)
            {
                Vector3 jmin = vList[0];
                Vector3 jmax = vList[0];

                for (int k = 1; k < vList.Count; k++)
                {
                    Vector3 v = vList[k];
                    if (v.x < jmin.x) jmin.x = v.x;
                    if (v.y < jmin.y) jmin.y = v.y;
                    if (v.z < jmin.z) jmin.z = v.z;

                    if (v.x > jmax.x) jmax.x = v.x;
                    if (v.y > jmax.y) jmax.y = v.y;
                    if (v.z > jmax.z) jmax.z = v.z;
                }

                jointWidth = jmax - jmin;  // get_width(joint_i_vertices)
            }
            // else: jointWidth = zero 유지 (해당 joint에 vertex가 거의 없는 경우)

            int baseIdx = j * 3;
            shapeVector[baseIdx + 0] = jointWidth.x / fwX;
            shapeVector[baseIdx + 1] = jointWidth.y / fwY;
            shapeVector[baseIdx + 2] = jointWidth.z / fwZ;

            // Debug.Log($"joint {j}: ({shapeVector[baseIdx+0]}, {shapeVector[baseIdx+1]}, {shapeVector[baseIdx+2]})");
        }

        return shapeVector;
    }
}
