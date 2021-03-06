﻿using UnityEngine;
using System.Collections;
using System.Linq;
using System.Collections.Generic;

//
// Data structures for runtime
//

[System.Serializable]
public class ControllerState
{
	public int uniqueNameHash;
	public string uniqueName;
	public string name;
}

public enum ControllerParameterType
{
	#if UNITY_4_2
	Vector,
	#else
	Trigger,
	#endif
	Float,
	Int,
	Bool
}

[System.Serializable]
public class ControllerStateMachine
{
	public string name;
	public int hash;
	public ControllerState[] 			states;
	public ControllerStateMachine[] 	subStateMachines;
}

[System.Serializable]
public class ControllerParameter
{
	public string name;
	public ControllerParameterType type;
}

[System.Serializable]
public class ControllerLayer
{
	public string name;
	public int hash;
	public ControllerStateMachine 		stateMachine;
}

[System.Serializable]
public class Controller
{
	public RuntimeAnimatorController 	controller;
	//public ControllerState[] 			states;
	public ControllerParameter[]		parameters;
	public ControllerLayer[]			layers;
}

//
// Database (contains building code)
// gets added into every scene as a gameobject
//

public class RuntimeAnimatorControllerExtensionData : MonoBehaviour
{
	[SerializeField] Controller[] controllers;
	
	static RuntimeAnimatorControllerExtensionData _instance = null;	
	static RuntimeAnimatorControllerExtensionData Instance 
	{
		get 
		{
			if(!_instance)
				_instance = (RuntimeAnimatorControllerExtensionData)FindObjectOfType(typeof(RuntimeAnimatorControllerExtensionData));
			
			return _instance;
		}
	}	
	
	Dictionary<RuntimeAnimatorController, Controller> _controllersTable;
	static Dictionary<RuntimeAnimatorController, Controller> ControllersTable 
	{
		get 
		{
			if(Instance._controllersTable==null)
			{
				Instance._controllersTable = new Dictionary<RuntimeAnimatorController, Controller>();
				foreach(var i in Instance.controllers)
				{
					Instance._controllersTable.Add(i.controller,i);
				}
			}
			
			return Instance._controllersTable;
		}
	}	
	
	#if UNITY_EDITOR
	[UnityEditor.Callbacks.PostProcessScene]
	static void BuildTable()
	{
		if(FindObjectOfType(typeof(RuntimeAnimatorControllerExtensionData)))
			return;
		
		GameObject obj = new GameObject("_RuntimeAnimatorControllerExtensionData");
		var table = obj.AddComponent<RuntimeAnimatorControllerExtensionData>();
		
		var list = new List<Controller>();
		foreach(var i in UnityEditor.AssetDatabase.GetAllAssetPaths().Where(j => System.IO.Path.GetExtension(j).ToLower()==".controller"))
		{
			var ac = (UnityEditorInternal.AnimatorController)UnityEditor.AssetDatabase.LoadAssetAtPath(i, typeof(UnityEditorInternal.AnimatorController));
			if(!ac) continue;
			
			var entry = new Controller();
			list.Add(entry);
			entry.controller 	= ac;
			entry.parameters 	= ac.EnumerateParameters().Select((param) => new ControllerParameter() { name = param.name, type = ConvertParameterType(param.type)}).ToArray();
			var states	 	= ac.EnumerateStatesRecursive().Select((arg) => new ControllerState() { name = arg.name, uniqueName = arg.uniqueName, uniqueNameHash = arg.uniqueNameHash}).ToArray();
			entry.layers 	 	= ac.EnumerateLayers().Select((arg) => new ControllerLayer() {name = arg.name, hash=Animator.StringToHash(arg.name), stateMachine = ConverStateMachine(arg.stateMachine, states)}).ToArray();
		}
		
		table.controllers = list.ToArray();
	}	

	static ControllerStateMachine ConverStateMachine(UnityEditorInternal.StateMachine stateMachine, ControllerState[] states)
	{
		return new ControllerStateMachine() {
			name = stateMachine.name,
			hash = Animator.StringToHash(stateMachine.name),
			states = states.Where(i => stateMachine.EnumerateStates().Any(j => j.uniqueNameHash==i.uniqueNameHash)).ToArray(),
			subStateMachines = Enumerable.Range(0, stateMachine.stateMachineCount).Select(i => ConverStateMachine(stateMachine.GetStateMachine(i),states)).ToArray()
		};
	}
	
	static ControllerParameterType ConvertParameterType(UnityEditorInternal.AnimatorControllerParameterType paramType)
	{
		switch(paramType)
		{
		case UnityEditorInternal.AnimatorControllerParameterType.Bool: 		return ControllerParameterType.Bool;
		case UnityEditorInternal.AnimatorControllerParameterType.Float: 	return ControllerParameterType.Float;
			#if UNITY_4_2
		case UnityEditorInternal.AnimatorControllerameterType.Vector: 		return ControllerParameterType.Vector;
			#else 
		case UnityEditorInternal.AnimatorControllerParameterType.Trigger: 		return ControllerParameterType.Trigger;
			#endif
		case UnityEditorInternal.AnimatorControllerParameterType.Int: 		return ControllerParameterType.Int;
			
		}
		throw new System.Exception("Unknown type");
	}
	#endif
	
	public static IEnumerable<ControllerParameter> GetParameters(RuntimeAnimatorController rac)
	{
		var controller = ControllersTable[rac];
		return controller.parameters.AsEnumerable();
	}
	
	public static IEnumerable<ControllerState> GetStates(RuntimeAnimatorController rac)
	{
		var controller = ControllersTable[rac];
		return controller.layers.SelectMany(i => GetStatesRecursive(i.stateMachine));
	}

	public static IEnumerable<ControllerState> GetStatesRecursive(ControllerStateMachine sm)
	{
		foreach(var i in sm.states.AsEnumerable())
			yield return i;

		foreach(var i in sm.subStateMachines.SelectMany(i => GetStatesRecursive(i)))
			yield return i;
	}

	public static IEnumerable<ControllerLayer> GetLayers(RuntimeAnimatorController rac)
	{
		var controller = ControllersTable[rac];
		return controller.layers.AsEnumerable();
	}
	
	public static int GetLayerCount(RuntimeAnimatorController rac)
	{
		var controller = ControllersTable[rac];
		return controller.layers.Length;
	}
}

// Extensions to the animation controller class
// these are helper functions used to build the database.
#if UNITY_EDITOR
public static class AnimationControllerExtensions
{
	public static IEnumerable<UnityEditorInternal.State> EnumerateStates(this UnityEditorInternal.StateMachine sm) 
	{
		for(int i=0; i<sm.stateCount; ++i)
		{
			yield return sm.GetState(i);
		}
	}

	public static IEnumerable<UnityEditorInternal.State> EnumerateStatesRecursive(this UnityEditorInternal.AnimatorController ac)
	{
		for(int i=0; i<ac.layerCount;++i)
		{
			var layer = ac.GetLayer(i);
			foreach(var j in EnumerateStatesRecursive(layer.stateMachine))
				yield return j;
		}
	}
	
	public static IEnumerable<UnityEditorInternal.State> EnumerateStatesRecursive(this UnityEditorInternal.StateMachine sm) 
	{
		foreach(var i in EnumerateStates(sm))
			yield return i;
		
		for(int i=0; i<sm.stateMachineCount; ++i)
		{
			foreach(var j in EnumerateStatesRecursive(sm.GetStateMachine(i)))
				yield return j;
		}
	}
	
	public static IEnumerable<KeyValuePair<string, string>> EnumerateStateNamesRecursive(this UnityEditorInternal.AnimatorController ac)
	{
		for(int i=0; i<ac.layerCount;++i)
		{
			var layer = ac.GetLayer(i);
			foreach(var j in EnumerateStateNamesRecursive(layer.stateMachine))
				yield return j;
		}
	}
	
	public static IEnumerable<KeyValuePair<string, string>> EnumerateStateNamesRecursive(this UnityEditorInternal.StateMachine sm) 
	{
		for(int i=0; i<sm.stateCount; ++i)
		{
			yield return new KeyValuePair<string, string>(sm.name+"."+sm.GetState(i).name, sm.GetState(i).name);
		}
		
		for(int i=0; i<sm.stateMachineCount; ++i)
		{
			var ssm = sm.GetStateMachine(i);
			foreach(var j in EnumerateStateNamesRecursive(ssm))
				yield return j;
		}
	}
	
	public static IEnumerable<UnityEditorInternal.AnimatorControllerParameter> EnumerateParameters(this UnityEditorInternal.AnimatorController ac)
	{
		for(int i=0; i<ac.parameterCount; ++i)
		{
			yield return ac.GetParameter(i);
		}
	}
	
	public static IEnumerable<UnityEditorInternal.AnimatorControllerLayer> EnumerateLayers(this UnityEditorInternal.AnimatorController ac)
	{
		for(int i=0; i<ac.layerCount; ++i)
		{
			yield return ac.GetLayer(i);
		}
	}
}
#endif
