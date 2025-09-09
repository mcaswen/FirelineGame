using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEditor;

namespace Unity.DemoTeam.DigitalHuman
{
    [CustomEditor(typeof(PatchVertexDataScriptableObject))]
    public class PatchVertexDataEditor : Editor
    {
        [SerializeField] string vertexDataPath; 
        [SerializeField] private int totalVertices;
        
        public override void OnInspectorGUI()
        {
            PatchVertexDataScriptableObject tool = (PatchVertexDataScriptableObject)target;
            
            base.OnInspectorGUI();
            
            if (GUILayout.Button("Load Data"))
            {
                List<List<int>> _mRenderPatches = new List<List<int>>();
                List<string> m_RenderPathesString = new List<string>();
            
                totalVertices = 0;
                using (var reader = new BinaryReader(new FileStream(tool.assetPath, FileMode.Open)))
                {
                    while(reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        var size = reader.ReadInt32();

                        string vtx = "";
                        List<int> patch = new List<int>();
                        for (int i = 0; i < size; i++)
                        {
                            int vtxID = reader.ReadInt32();
                            patch.Add(vtxID);
                            if (i == size - 1)
                            {
                                vtx += vtxID.ToString(); 
                            }
                            else
                            {
                                vtx += vtxID.ToString() + ","; 
                            }
                            
                        }
                            
                        m_RenderPathesString.Add(vtx);
                        _mRenderPatches.Add(patch);
                        totalVertices += size;
                    }
                    
                    tool.SetVertexStringData(m_RenderPathesString);
                    // tool.SetPatchData(_mRenderPatches);
                    tool.VertexCount = totalVertices;

                    UnityEditor.EditorUtility.SetDirty(tool);
                    UnityEditor.AssetDatabase.SaveAssetIfDirty(tool);
                }
                
                
                
            }
        }
    }
}