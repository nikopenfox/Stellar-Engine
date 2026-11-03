using UnityEngine;
using UnityEditor;

/// <summary>
/// 共通重心(StarSystemAsset.BaryCenterData)のインスペクタ。
/// 相対軌道長半径・天体Aの軌道長半径・天体Bの軌道長半径のどれかを変えると、天体A・Bの質量から残りの2つを計算し直す。
/// a = aA + aB、aA = a × mB / (mA + mB)、aB = a × mA / (mA + mB)
/// </summary>
[CustomPropertyDrawer(typeof(StarSystemAsset.BaryCenterData))]
public class BaryCenterDataDrawer : PropertyDrawer
{
    private const string REL = "relativeSemiMajorAxis";
    private const string SMA_A = "semiMajorAxisA";
    private const string SMA_B = "semiMajorAxisB";

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight; //折りたたみの見出し
        if (!property.isExpanded) { return height; }

        var child = property.Copy();
        var end = property.GetEndProperty();
        child.NextVisible(true);
        while (!SerializedProperty.EqualContents(child, end))
        {
            height += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;
            if (!child.NextVisible(false)) { break; }
        }
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, label, true);
        rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;

            string changed = null; //変更された軌道長半径の名前
            var child = property.Copy();
            var end = property.GetEndProperty();
            child.NextVisible(true);
            while (!SerializedProperty.EqualContents(child, end))
            {
                rect.height = EditorGUI.GetPropertyHeight(child, true);

                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(rect, child, true);
                if (EditorGUI.EndChangeCheck() && (child.name == REL || child.name == SMA_A || child.name == SMA_B))
                {
                    changed = child.name;
                }

                rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
                if (!child.NextVisible(false)) { break; }
            }

            if (changed != null) { LinkSemiMajorAxes(property, changed); }

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    //変更された軌道長半径から、残りの2つを質量比で求める
    private void LinkSemiMajorAxes(SerializedProperty property, string changed)
    {
        var asset = property.serializedObject.targetObject as StarSystemAsset;
        if (asset == null) { return; }

        double massA = asset.GetBodyMass(property.FindPropertyRelative("bodyA").stringValue);
        double massB = asset.GetBodyMass(property.FindPropertyRelative("bodyB").stringValue);
        double total = massA + massB;
        if (total <= 0) { return; } //質量が分からなければ連動させない

        var rel = property.FindPropertyRelative(REL);
        var smaA = property.FindPropertyRelative(SMA_A);
        var smaB = property.FindPropertyRelative(SMA_B);

        double a; //相対軌道長半径
        switch (changed)
        {
            case SMA_A:
                if (massB <= 0) { return; } //天体Bの質量が0だと天体Aは重心から動かないので、相対軌道長半径を決められない
                a = smaA.doubleValue * total / massB;
                break;
            case SMA_B:
                if (massA <= 0) { return; }
                a = smaB.doubleValue * total / massA;
                break;
            default:
                a = rel.doubleValue;
                break;
        }

        rel.doubleValue = a;
        smaA.doubleValue = a * massB / total;
        smaB.doubleValue = a * massA / total;
    }
}
