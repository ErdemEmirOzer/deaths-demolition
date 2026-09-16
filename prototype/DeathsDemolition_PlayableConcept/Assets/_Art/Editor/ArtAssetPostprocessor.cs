using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Applies <see cref="ArtImportContract"/> to every model and texture under
    /// <c>Assets/_Art</c> at import time. Runs on first import and on every re-import, so
    /// an Inspector edit is overwritten the next time the asset imports and the gate test
    /// catches it in between. Assets outside <c>Assets/_Art</c> are untouched.
    ///
    /// Idempotent: the contract is only written when a field differs, so importing a
    /// compliant asset never dirties its .meta.
    /// </summary>
    public sealed class ArtAssetPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion()
        {
            // Bumping this re-imports every asset the postprocessor touches. It is tied to
            // the contract version so a contract change cannot leave stale artifacts.
            // 2: WP-015 texture class table (ADR-015-1). 3: WP-016 skinned characters (ADR-016-1). 4: WP-018 AP-009 clip events (ADR-018-1).
            return 4;
        }

        private void OnPreprocessModel()
        {
            if (!ArtImportContract.IsContractModel(assetPath))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            // WP-016: skinned characters take the v1.1 extension (Humanoid avatar, clips, no optimisation).
            bool changed = ArtCharacterContract.IsCharacterModel(assetPath)
                ? ArtCharacterContract.Apply(importer)
                : ArtImportContract.Apply(importer);
            if (changed)
            {
                Debug.Log("[ArtImportContract] applied to " + assetPath);
            }
        }

        /// <summary>
        /// WP-016: the takes of a character Clips/ file exist only once the model is parsed, so
        /// the loop / root-lock flags are written here (defaultClipAnimations is empty in
        /// OnPreprocessModel on a first import).
        /// </summary>
        private void OnPreprocessAnimation()
        {
            if (!ArtCharacterContract.IsClip(assetPath))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            if (ArtCharacterContract.ApplyClipsFor(importer))
            {
                Debug.Log("[ArtImportContract] clips applied to " + assetPath);
            }
        }

        private void OnPreprocessTexture()
        {
            if (!ArtImportContract.IsContractTexture(assetPath))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            ArtImportContract.TextureRole role = ArtImportContract.RoleOf(assetPath);
            if (ArtImportContract.Apply(importer, role))
            {
                Debug.Log("[ArtImportContract] applied (" + role + ") to " + assetPath);
            }
        }
    }
}
