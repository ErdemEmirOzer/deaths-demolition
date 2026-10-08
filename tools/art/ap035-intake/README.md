# AP-035 final logo bake intake (S-11, C-00 08.10)

Final content preserved from c69f8e464a97e361d0df16402fb64dfa444beb32. Source WIP commit is not transported.
Unity integration uses Logo wordmark/time map at 2048, halo at 1024, and five LogoMark sprites under the general 1024 class.
Unity-generated metadata and the repeatable UiArtImporter accompany the final Assets files.
- bake_ap035_logo.py: the existing deterministic 36-stroke AP-035 v002 masks/timeline and v003 motion spec bake; input/output hashes in out/bake-report.json. No bake rerun.
- preview_ap035_logo.py: existing review helper; preview files remain in KADRO.
- out/: only the final eight PNG files plus the original bake report and stdout. Marketing cover/trailer files stay outside Unity.
The new combined menu uses one callback-driven UiLogoMenuIntro and one installer Ensure; its MainMenuScreen owns input and skip.
Combined compile/runtime acceptance is pending C-00; prior EM/PM evidence describes the old source only.
