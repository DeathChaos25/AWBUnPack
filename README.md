# AWBUnPack
For extracting raw audio data from AWB files (i.e. extract raw adx/hca) and repackaging output data into new AWB  

(Note: Dotnet 8.0 is required to run this program!)  

# Simple Usage  
- EXTRACT AWB: Drag and Drop a .AWB file into program exe to extract files to folder
- CREATE AWB: Drag and Drop folder with audio files into program exe to pack into a .AWB file  
  
# Commandline Usage  
- To unpack an AWB: AWBUnPack [path to .AWB file] 
- To Repack an AWB: AWBUnPack [path to folder with audio files] -swi (optional argument, writes wave IDs to header as shorts)  
