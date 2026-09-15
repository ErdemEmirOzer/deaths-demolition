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
            // 2: WP-015 texture class table (ADR-015-1).
            return 2;
        }

        private void OnPreprocessModel()
        {
            if (!ArtImportContract.IsContractModel(assetPath))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            if (ArtImportContract.Apply(importer))
            {
                Debug.Log("[ArtImportContract] applied to " + assetPath);
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
