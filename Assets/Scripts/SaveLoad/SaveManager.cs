using UnityEngine;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Collections.Generic;
using System;


namespace VNEngine
{
    // This class is static, so you can call it from anywhere.
    // Based on this tutorial: http://gamedevelopment.tutsplus.com/tutorials/how-to-save-and-load-your-players-progress-in-unity--cms-20934
    public static class SaveManager
    {
        // All saved games (used by legacy UILoadSaveController)
        public static List<SaveFile> saved_games = new List<SaveFile>();

        // Which slot the current playthrough is using (0, 1, or 2)
        public static int current_slot = 0;

        // Legacy shared save file (kept for UILoadSaveController compatibility)
        private static string save_file_name = "saved_games.gd";
        private static string full_save_file_path = Application.persistentDataPath + "/" + save_file_name;

        // Per-slot file paths — each slot is isolated so corruption in one never affects another
        private static string SlotPath(int slot) =>
            Application.persistentDataPath + "/save_slot_" + slot + ".gd";


        // Returns the SaveFile for the given slot from its own file, or null if the slot is empty
        public static SaveFile GetSaveForSlot(int slot)
        {
            string path = SlotPath(slot);
            if (!File.Exists(path)) return null;
            BinaryFormatter bf = new BinaryFormatter();
            FileStream file = File.Open(path, FileMode.Open);
            try
            {
                SaveFile save = (SaveFile)bf.Deserialize(file);
                file.Close();
                return save;
            }
            catch (Exception e)
            {
                Debug.LogError("Exception loading save slot " + slot + ", deleting: " + e);
                file.Close();
                File.Delete(path);
                return null;
            }
        }

        // Writes the save to the current slot's own file
        public static void AddNewSave(SaveFile current)
        {
            current.slot_index = current_slot;

            try
            {
                BinaryFormatter bf = new BinaryFormatter();
                FileStream file = File.Create(SlotPath(current_slot));
                bf.Serialize(file, current);
                file.Close();
                Debug.Log("Saved slot " + current_slot + " to: " + SlotPath(current_slot));
            }
            catch (Exception e)
            {
                Debug.LogError("Exception saving slot " + current_slot + ": " + e);
            }

            // Also maintain the legacy list so UILoadSaveController still works
            LoadFromFile();
            saved_games.RemoveAll(s => s.slot_index == current_slot);
            saved_games.Add(current);
            Save();
        }

        // Saves the legacy shared list to disk (used by UILoadSaveController)
        public static void Save()
        {
            Debug.Log("Saving legacy file: " + full_save_file_path);
            BinaryFormatter bf = new BinaryFormatter();
            FileStream file = File.Create(full_save_file_path);
            bf.Serialize(file, SaveManager.saved_games);
            file.Close();
        }

        // Loads the legacy shared list from disk (used by UILoadSaveController)
        public static void LoadFromFile()
        {
            if (File.Exists(full_save_file_path))
            {
                BinaryFormatter bf = new BinaryFormatter();
                FileStream file = File.Open(full_save_file_path, FileMode.Open);
                try
                {
                    SaveManager.saved_games = (List<SaveFile>)bf.Deserialize(file);
                }
                catch (Exception e)
                {
                    Debug.LogError("Exception loading save file, deleting: " + e);
                    DeleteAllSaves();
                }
                file.Close();
            }
            else
                Debug.Log("Could not find save file: " + full_save_file_path);
        }

        // Deletes the slot's own file and removes it from the legacy list
        public static void DeleteSave(SaveFile save)
        {
            string path = SlotPath(save.slot_index);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log("Deleted save slot " + save.slot_index);
            }
            saved_games.Remove(save);
            Save();
        }

        public static void DeleteAllSaves()
        {
            for (int i = 0; i < 3; i++)
            {
                string path = SlotPath(i);
                if (File.Exists(path)) File.Delete(path);
            }
            if (File.Exists(full_save_file_path))
            {
                File.Delete(full_save_file_path);
                Debug.Log("All save files deleted");
            }
            saved_games.Clear();
        }



        public static string GetGameObjectPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }



        // Call this to store the node that is running so it can executed when loaded
        // Used by SetBackground, SetBackgroundTransparent, StaticImageNode, Music nodes
        public static void SetSaveFeature(Node node_that_is_running, GameObject object_containing_save_feature)
        {
            bool found_same_feature_on_object = false;

            // Find all FeatureToSave components on this object
            foreach (FeatureToSave f in object_containing_save_feature.GetComponents<FeatureToSave>())
            {
                if (f.Type_of_Node_to_Execute.GetType() == node_that_is_running.GetType())
                {
                    found_same_feature_on_object = true;
                    f.SetFeature(node_that_is_running);
                    return;
                }
            }

            if (!found_same_feature_on_object)
            {
                FeatureToSave feature = object_containing_save_feature.AddComponent<FeatureToSave>();
                feature.SetFeature(node_that_is_running);
            }
        }
        // Call this to prevent a feature from being saved
        public static void RemoveSaveFeature(GameObject object_containing_save_feature)
        {
            FeatureToSave f = object_containing_save_feature.GetComponent<FeatureToSave>();
            if (f != null)
                GameObject.Destroy(f);
        }
        // Removes all SaveFeature components from the given object
        public static void DeleteSaveFeatures(GameObject object_containing_save_feature)
        {
            foreach (FeatureToSave f in object_containing_save_feature.GetComponents<FeatureToSave>())
            {
                GameObject.Destroy(f);
            }
        }
    }
}