!macro customInstall
  nsExec::ExecToLog '"$INSTDIR\resources\agent\pixelchat-helper.exe" path-add "$INSTDIR\resources\agent"'
!macroend

!macro customUnInstall
  nsExec::ExecToLog '"$INSTDIR\resources\agent\pixelchat-helper.exe" path-remove "$INSTDIR\resources\agent"'
!macroend
