### Configure and manage symbolic links to SMB shares on Windows

If you need more than 25 mapped network drives, hitting the Windows limitation of letters of the alphabet, this tool may help you.
First, I wrote DriveMapper, which takes the pain out of mapped network drives in Windows.  Then I had a use case for mapping more
remote SMB shares locally than Windows allows, so I wrote a new tool to use symbolic links instead of mapped network drives, enabling
an unlimited number of remote SMB shares to be mapped locally.  

Usage: ShareMapper.exe [map] [add] [delete] [update-password] [set-root-folder]

* Type `sharemapper add` to add a share to the program's config.
* Type `sharemapper delete` to remove a share from the program's config.
* Type `sharemapper update-password` to change the password for a share.
* Type `sharemapper map` to create the symbolic links for all configured shares.
* Type `sharemapper set-root-folder` to change the root folder for all symbolic links.

- Passwords are stored securely in Windows Credential Manager.
- The program will attempt to configure symbolic links to be persistent (survive reboots), however if storage of Windows credentials
is disabled on your system, you will need to run `sharemapper map` after a reboot to re-enable access.
- Note that creating symbolic links on Windows requires admin privileges, so you will need to run this program as an administrator.
- Note that it is safe to delete the symbolic links created by this program, doing so will not delete any files on the remote SMB share.
