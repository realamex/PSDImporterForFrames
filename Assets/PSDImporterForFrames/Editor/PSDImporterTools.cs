using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

using UnityEditor.AssetImporters;
using UnityEditor.U2D.Sprites;
using UnityEditor.U2D.Common;
using System.Reflection;
using Unity.Collections;
using Object = UnityEngine.Object;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.U2D.PSD;

namespace UnityEditor {
    public static class PSDImporterTools {
        static readonly Regex assetsPathRegex = new( @"Assets(?=[/\\]).+" );//������ʽ��ת���������C#ת��Ļ�������תһ�Σ�������@����
        public static string GetAssetPath( this string path, bool withExt = true ) {
            string assetPath;
            if ( withExt )
                assetPath = assetsPathRegex.Match( Path.GetFullPath( path ) ).Value;
            else {
                string fullPathWithOutExt = Path.Combine( Path.GetDirectoryName( path ), "\\", Path.GetFileNameWithoutExtension( path ) );
                assetPath = assetsPathRegex.Match( fullPathWithOutExt ).Value;
            }
            return assetPath;
        }
        /// <summary>IEnumerable<T>�Զ���ӷָ���תΪstring</summary>
        public static string ToStrings<T>( this IEnumerable<T> items, string separator = " | " ) {
            return string.Join( separator, items.ToArray() );
        }
        public static List<Object> GetObjs( this AssetImportContext ctx ) {
            var objs = new List<Object>();
            ctx.GetObjects( objs );
            return objs;
        }

        /// <summary>RectInt ת Rect</summary>
        public static Rect ToRect( this RectInt rectInt ) => new( rectInt.position, rectInt.size );
        /// <summary>����uv�������ɿ���ʹÿ��ͼ����unity�л�ԭpsd�ж�Ӧλ�õ�pivot��rect��pivot���½�����Ϊ00��</summary>
        public static Vector2 GetOriginPosInPsd( this Vector2 rectPos, Vector2 uvTransform ) => rectPos - uvTransform;//-uvʹ�仹ԭΪpsd�е�ԭʼ����
        /// <summary>����uv�������ɿ���ʹÿ��ͼ����unity�л�ԭpsd�ж�Ӧλ�õ�pivot��rect��pivot���½�����Ϊ00��</summary>
        public static Vector2 GetOriginPosInPsd( this Vector2Int rectPos, Vector2 uvTransform ) => rectPos - uvTransform;//-uvʹ�仹ԭΪpsd�е�ԭʼ����

        /// <summary>���䴴������List</summary>
        public static object ReflectionCreatList( this Type elementType, params object[] elements ) {
            var listType = typeof( List<> ).MakeGenericType( elementType );
            dynamic list = (IList)Activator.CreateInstance( listType );
            Array.ForEach( elements, x => list.Add( Convert.ChangeType( x, listType ) ) );
            return list;
        }

        [MenuItem( "Assets/Revert To Default Importer", false, 30 )]
        static void RevertToDefaultImporter() {
            var selectings = Selection.GetFiltered<Object>( SelectionMode.ExcludePrefab );
            foreach ( var obj in selectings ) {
                var path = AssetDatabase.GetAssetPath( obj );
                AssetDatabase.ClearImporterOverride( path );
                var defaultImporter = AssetDatabase.GetDefaultImporter( path );
                Debug.Log( $"Revert To Default Importer [{defaultImporter.Name}] @ {path}" );
            }
        }
        //[MenuItem( "PSDImporterTools/Reset All PSDImporterFrames", false, 30 )]
        //static void ResetAllPSDImporterFrames() {
        //    var allFilePaths = AssetDatabase.GetAllAssetPaths();
        //    var psdPaths = allFilePaths.Where( p => p.StartsWith( "Assets/" ) && ( p.EndsWith( ".psd" ) || p.EndsWith( ".psb" ) ) ).ToArray();
        //    foreach ( var path in psdPaths ) {
        //        var importerType = AssetDatabase.GetImporterOverride( path );
        //        if ( importerType == typeof( PSDImporterFrames ) ) {
        //            AssetDatabase.ClearImporterOverride( path );
        //            AssetDatabase.SetImporterOverride<PSDImporterFrames>( path );
        //        }
        //    }
        //}

        /// <summary>������һ������Ϊȫ͸���ҵ�ǰ���ز���ȫ͸�������ز���¼��index������ϵ�ʱ�Ա�nativeArray����</summary>
        public static int[] PixelIndexsFirstTimeNotTransparent( this IEnumerable<Color32> nativeArrayColor32 ) {
            List<int> indexs = new();
            bool lastIsTrans = true;
            bool thisIsTrans;
            var array = nativeArrayColor32.ToArray();
            for ( int i = 0; i < array.Length; i++ ) {
                thisIsTrans = array[i].a == 0;
                if ( lastIsTrans && !thisIsTrans ) {
                    indexs.Add( i );
                }
                lastIsTrans = thisIsTrans;
            }
            return indexs.ToArray();
        }
        /// <summary>������һ������Ϊȫ͸���ҵ�ǰ���ز���ȫ͸�������ز���¼��index������ϵ�ʱ�Ա�nativeArray����</summary>
        public static List<int[]> PixelIndexsFirstTimeNotTransparent( this IEnumerable<IEnumerable<Color32>> nativeArrayColor32s ) {
            List<int[]> arrays = new();
            foreach ( var nativeArray in nativeArrayColor32s ) {
                arrays.Add( nativeArray.PixelIndexsFirstTimeNotTransparent() );
            }
            return arrays;
        }
    }


    public enum PSDImporterType {
        ForFrames,
        UnityInternal,
        All
    }
    public class PSDBatchSettingWindow : EditorWindow {
        [MenuItem( "PSDImporterTools/Set All PSDImporters", false, 30 )]
        static void CreateProjectCreationWindow() {
            var window = CreateInstance<PSDBatchSettingWindow>();
            window.ShowUtility();
        }

        PSDImporterType psdImporterType = default;
        string pixelPerUnit;
        void OnGUI() {
            EditorGUILayout.LabelField( "Change All PSD's parameters" );
            pixelPerUnit = EditorGUILayout.TextField( "pixelPerUnit: ", pixelPerUnit );
            psdImporterType = (PSDImporterType)EditorGUILayout.EnumPopup( "psdImporterType: ", psdImporterType );

            if ( GUILayout.Button( "Apply" ) ) {
                if ( int.TryParse( pixelPerUnit, out int pixels ) ) {
                    SetAndApplyPSDImportersParams( pixels );
                }
            }
        }
        void SetAndApplyPSDImportersParams( int pixels ) {
            var notNulls = FilteredImporters.Where( i => i != null );
            foreach ( var importer in notNulls ) {
                importer.spritePixelsPerUnit = pixels;
                var ISEDP = importer as ISpriteEditorDataProvider;
                ISEDP.Apply();
            }
            var paths = notNulls.Select( i => i.assetPath );
            Debug.Log( $"{paths.Count()} PSD's pixelPerUnit Changed to [{pixels}] @ :  { paths.ToStrings()}" );
            AssetDatabase.Refresh();
        }
        PSDImporter[] FilteredImporters {
            get {
                var psdImporters = GetAllPSDImportersInAssets();
                var filteredImporters = psdImporterType switch
                {
                    PSDImporterType.ForFrames => psdImporters.Where( i => i.GetType() == typeof( PSDImporterFrames ) ),
                    PSDImporterType.UnityInternal => psdImporters.Where( i => i.GetType() == typeof( PSDImporter ) ),
                    PSDImporterType.All => psdImporters,
                    _ => throw new NotImplementedException(),
                };
                return filteredImporters.ToArray();
            }
        }
        PSDImporter[] GetAllPSDImportersInAssets() {
            var results = new List<PSDImporter>();
            var allFilePaths = AssetDatabase.GetAllAssetPaths();
            var psdPaths = allFilePaths.Where( p => p.StartsWith( "Assets/" ) && ( p.EndsWith( ".psd" ) || p.EndsWith( ".psb" ) ) ).ToArray();
            foreach ( var path in psdPaths ) {
                var importer = AssetImporter.GetAtPath( path ) as PSDImporter;
                if ( importer ) results.Add( importer );
            }
            return results.ToArray();
        }

    }
}