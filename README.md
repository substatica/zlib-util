## Unreal Engine Save zlib Utility

This is a console based utility for extracting and compressing Unreal Engine save files which have been compressed into zlib chunks.

*Usage*

Run the app from a commandline use the -x flag to extract a file,

./zlib-util.exe -x MySave.save

This will extract the save file to **MySave.save.extracted**.

Use the -c flag in conjunction with the -r flag to compress a file. The -r flag is used to set a compressed reference save file, in most cases this should be the original compressed save which was extracted. This reference file is used to determine max chunk size and to replicate header bytes with as-of-yet unknown purposes.

./zlib-util.exe -c MySave.save.extracted -r MySave.save

This will compress the save file based on the reference save to **MySave.save.extracted.compressed**.

You can also specify the compression level with -l from 0 to 9. I have found the default, which is -l 6, results in the closest binary match to the files I have tested with.

http://paypal.me/substatica
