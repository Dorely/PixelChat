// PixelChat MIT. Windows-only agent helper: DPAPI bridge discovery, private ACLs, and installer PATH ownership.
#include <windows.h>
#include <wincrypt.h>
#include <aclapi.h>
#include <sddl.h>
#include <fcntl.h>
#include <io.h>
#include <cstdio>
#include <vector>
#include <string>
#include <algorithm>

static int fail(const char* message) { fprintf(stderr, "%s\n", message); return 1; }
static int protect(bool decrypt) {
    std::vector<unsigned char> bytes; unsigned char block[4096]; size_t n;
    while((n=fread(block,1,sizeof(block),stdin))) { bytes.insert(bytes.end(),block,block+n); if(bytes.size()>65536) return fail("Discovery data too large."); }
    DATA_BLOB in{(DWORD)bytes.size(),bytes.data()}, out{};
    BOOL ok=decrypt ? CryptUnprotectData(&in,nullptr,nullptr,nullptr,nullptr,CRYPTPROTECT_UI_FORBIDDEN,&out)
                    : CryptProtectData(&in,L"PixelChat local bridge",nullptr,nullptr,nullptr,CRYPTPROTECT_UI_FORBIDDEN,&out);
    if(!ok) return fail("Windows could not protect/unprotect bridge discovery.");
    bool written=fwrite(out.pbData,1,out.cbData,stdout)==out.cbData;
    SecureZeroMemory(out.pbData,out.cbData); LocalFree(out.pbData);
    return written ? 0 : fail("Discovery write failed.");
}
static int secureDirectory(wchar_t* path) {
    HANDLE token; if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)) return fail("Cannot resolve current Windows user.");
    DWORD size=0; GetTokenInformation(token,TokenUser,nullptr,0,&size); std::vector<unsigned char> data(size);
    if(!GetTokenInformation(token,TokenUser,data.data(),size,&size)) { CloseHandle(token); return fail("Cannot resolve user SID."); }
    CloseHandle(token); LPWSTR sid=nullptr;
    if(!ConvertSidToStringSidW(((TOKEN_USER*)data.data())->User.Sid,&sid)) return fail("Cannot format user SID.");
    std::wstring sddl=L"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;"; sddl+=sid; sddl+=L")"; LocalFree(sid);
    PSECURITY_DESCRIPTOR descriptor=nullptr;
    if(!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(),SDDL_REVISION_1,&descriptor,nullptr)) return fail("Cannot create private ACL.");
    BOOL present,defaulted; PACL acl; GetSecurityDescriptorDacl(descriptor,&present,&acl,&defaulted);
    DWORD error=SetNamedSecurityInfoW(path,SE_FILE_OBJECT,DACL_SECURITY_INFORMATION|PROTECTED_DACL_SECURITY_INFORMATION,nullptr,nullptr,acl,nullptr);
    LocalFree(descriptor); return error==ERROR_SUCCESS ? 0 : fail("Cannot restrict bridge discovery directory.");
}
// Keep the existing registry value type, entry spelling and empty PATH segments.
static int updatePath(const wchar_t* directory, bool remove) {
    HKEY environment, ownership;
    if (RegCreateKeyExW(HKEY_CURRENT_USER,L"Environment",0,nullptr,0,KEY_QUERY_VALUE|KEY_SET_VALUE,nullptr,&environment,nullptr)!=ERROR_SUCCESS) return fail("Cannot open user environment.");
    if (RegCreateKeyExW(HKEY_CURRENT_USER,L"Software\\PixelChat",0,nullptr,0,KEY_QUERY_VALUE|KEY_SET_VALUE,nullptr,&ownership,nullptr)!=ERROR_SUCCESS) { RegCloseKey(environment); return fail("Cannot open installation record."); }
    auto read = [](HKEY key,const wchar_t* name,std::wstring& value,DWORD& type) {
        DWORD bytes=0;
        LSTATUS error=RegGetValueW(key,nullptr,name,RRF_RT_REG_SZ|RRF_RT_REG_EXPAND_SZ|RRF_NOEXPAND,&type,nullptr,&bytes);
        if(error==ERROR_FILE_NOT_FOUND) { type=REG_EXPAND_SZ; return true; }
        if(error!=ERROR_SUCCESS) return false;
        std::vector<wchar_t> buffer(bytes/sizeof(wchar_t)+1,0);
        if(RegGetValueW(key,nullptr,name,RRF_RT_REG_SZ|RRF_RT_REG_EXPAND_SZ|RRF_NOEXPAND,&type,buffer.data(),&bytes)!=ERROR_SUCCESS) return false;
        value=buffer.data(); return true;
    };
    std::wstring path,owned,target=directory; DWORD pathType,ownedType;
    if(!read(environment,L"Path",path,pathType) || !read(ownership,L"AgentPath",owned,ownedType)) { RegCloseKey(ownership); RegCloseKey(environment); return fail("Cannot read user PATH safely."); }
    std::vector<std::wstring> parts; size_t offset=0;
    if(!path.empty()) do { size_t end=path.find(L';',offset); if(end==std::wstring::npos) end=path.size(); parts.push_back(path.substr(offset,end-offset)); offset=end+1; } while(offset<=path.size());
    auto same = [](const std::wstring& a,const std::wstring& b) { return !_wcsicmp(a.c_str(),b.c_str()); };
    bool present=std::any_of(parts.begin(),parts.end(),[&](const auto& p){return same(p,target);});
    bool changed=false;
    if(remove && same(owned,target)) {
        parts.erase(std::remove_if(parts.begin(),parts.end(),[&](const auto& p){return same(p,target);}),parts.end()); changed=true;
    } else if(!remove && !present) {
        if(!owned.empty()) parts.erase(std::remove_if(parts.begin(),parts.end(),[&](const auto& p){return same(p,owned);}),parts.end());
        parts.push_back(target); changed=true;
    }
    DWORD error=ERROR_SUCCESS;
    if(changed) {
        std::wstring next; for(size_t i=0;i<parts.size();i++) { if(i) next+=L';'; next+=parts[i]; }
        error=RegSetValueExW(environment,L"Path",0,pathType,(const BYTE*)next.c_str(),(DWORD)((next.size()+1)*sizeof(wchar_t)));
        if(error==ERROR_SUCCESS) {
            if(remove) RegDeleteValueW(ownership,L"AgentPath");
            else error=RegSetValueExW(ownership,L"AgentPath",0,REG_SZ,(const BYTE*)target.c_str(),(DWORD)((target.size()+1)*sizeof(wchar_t)));
            DWORD_PTR result; SendMessageTimeoutW(HWND_BROADCAST,WM_SETTINGCHANGE,0,(LPARAM)L"Environment",SMTO_ABORTIFHUNG,2000,&result);
        }
    }
    RegCloseKey(ownership); RegCloseKey(environment); return error==ERROR_SUCCESS ? 0 : fail("Cannot update user PATH or installation record.");
}
int wmain(int argc,wchar_t** argv) {
    _setmode(_fileno(stdin),_O_BINARY); _setmode(_fileno(stdout),_O_BINARY);
    if(argc==2 && !wcscmp(argv[1],L"protect")) return protect(false);
    if(argc==2 && !wcscmp(argv[1],L"unprotect")) return protect(true);
    if(argc==3 && !wcscmp(argv[1],L"secure-directory")) return secureDirectory(argv[2]);
    if(argc==3 && !wcscmp(argv[1],L"path-add")) return updatePath(argv[2],false);
    if(argc==3 && !wcscmp(argv[1],L"path-remove")) return updatePath(argv[2],true);
    return fail("Expected protect, unprotect, secure-directory PATH, path-add DIR, or path-remove DIR.");
}
