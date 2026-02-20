---
trigger: always_on
description: Development rules for Background Muter
---

- Implement one feature at a time
- Add each feature to the to-do list
- Commit and push after each feature
- Create separate branches for each feature
- Merge only if the feature works correctly
- Test build after each feature
     taskkill /F /IM WinBGMuter.exe; dotnet build "c:\Windows_software\Background-Muter\Background Muter.sln" -c Release
- Fix any build errors before moving to the next feature
- Run tests after each build
- Document each feature in the README














