using Elements.Core;
using FrooxEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Rendering;
using UnityEngine;
using UnityFrooxEngineRunner;
using SharpDX.DXGI;

namespace Thundagun.NewConnectors.ComponentConnectors;

public class ReflectionProbeSH2Connector : ComponentConnectorSingle<ReflectionProbeSH2>
{
	public enum ComputeResult
	{
		Computed,
		Postpone,
		Failed
	}

	private static ComputeShader _compute;

	public static Vector4[] output = new Vector4[9];

	private static int _ReduceKernel;

	private static int[] _SHkernels = new int[9];

	private static ComputeBuffer[] _buffers = new ComputeBuffer[2];

	private UnityEngine.RenderTexture _convertTexture;

	public override void ApplyChanges()
	{
		Thundagun.QueuePacket(new ApplyChangesReflectionProbeSH2Connector(this));
	}

	public override void DestroyMethod(bool destroyingWorld)
	{
		if (_convertTexture != null)
		{
			UnityEngine.Object.DestroyImmediate(_convertTexture);
			_convertTexture = null;
		}
		base.DestroyMethod(destroyingWorld);
	}

	public ComputeResult ComputeFromProbe(FrooxEngine.ReflectionProbe probe, Vector4[] output)
	{
		if (probe == null)
		{
			return ComputeResult.Failed;
		}
		UnityEngine.ReflectionProbe reflectionProbe = (probe?.Connector as ReflectionProbeConnector)?.UnityProbe;
		if (reflectionProbe == null)
		{
			return ComputeResult.Failed;
		}
		if (probe.ProbeType.Value == FrooxEngine.ReflectionProbe.Type.Realtime || probe.ProbeType.Value == FrooxEngine.ReflectionProbe.Type.OnChanges)
		{
			UnityEngine.RenderTexture realtimeTexture = reflectionProbe.realtimeTexture;
			if (realtimeTexture == null)
			{
				return ComputeResult.Postpone;
			}
			if (!GPU_Project_Uniform_9Coeff(realtimeTexture, output, ref _convertTexture))
			{
				return ComputeResult.Failed;
			}
			return ComputeResult.Computed;
		}
		if (probe.ProbeType.Value == FrooxEngine.ReflectionProbe.Type.Baked)
		{
			UnityEngine.Cubemap cubemap = reflectionProbe.customBakedTexture as UnityEngine.Cubemap;
			if (cubemap != null)
			{
				if (!GPU_Project_Uniform_9Coeff(cubemap, output, ref _convertTexture))
				{
					return ComputeResult.Failed;
				}
				return ComputeResult.Computed;
			}
		}
		return ComputeResult.Failed;
	}

	public static bool GPU_Project_Uniform_9Coeff(UnityEngine.RenderTexture input, Vector4[] output, ref UnityEngine.RenderTexture currentTexture)
	{
		RenderTextureDescriptor desc = default(RenderTextureDescriptor);
		desc.autoGenerateMips = false;
		desc.bindMS = false;
		desc.colorFormat = input.format;
		desc.depthBufferBits = 0;
		desc.dimension = TextureDimension.Tex2DArray;
		desc.enableRandomWrite = false;
		desc.height = input.height;
		desc.width = input.width;
		desc.msaaSamples = 1;
		desc.sRGB = true;
		desc.useMipMap = false;
		desc.volumeDepth = 6;
		if (currentTexture == null || currentTexture.descriptor.colorFormat != desc.colorFormat || currentTexture.descriptor.height != desc.height || currentTexture.descriptor.width != desc.width)
		{
			if (currentTexture != null)
			{
				UnityEngine.Object.DestroyImmediate(currentTexture);
			}
			currentTexture = new UnityEngine.RenderTexture(desc);
			currentTexture.Create();
		}
		for (int i = 0; i < 6; i++)
		{
			Graphics.CopyTexture(input, i, 0, currentTexture, i, 0);
		}
		return Render_GPU_Project_Uniform_9Coeff(currentTexture, output);
	}

	public static bool GPU_Project_Uniform_9Coeff(UnityEngine.Cubemap input, Vector4[] output, ref UnityEngine.RenderTexture currentTexture)
	{
		RenderTextureFormat? renderTextureFormat = ConvertRenderFormat(input.format);
		if (!renderTextureFormat.HasValue)
		{
			return false;
		}
		RenderTextureDescriptor desc = default(RenderTextureDescriptor);
		desc.autoGenerateMips = false;
		desc.bindMS = false;
		desc.colorFormat = renderTextureFormat.Value;
		desc.depthBufferBits = 0;
		desc.dimension = TextureDimension.Tex2DArray;
		desc.enableRandomWrite = false;
		desc.height = input.height;
		desc.width = input.width;
		desc.msaaSamples = 1;
		desc.sRGB = true;
		desc.useMipMap = false;
		desc.volumeDepth = 6;
		if (currentTexture == null || currentTexture.descriptor.colorFormat != desc.colorFormat || currentTexture.descriptor.height != desc.height || currentTexture.descriptor.width != desc.width)
		{
			if (currentTexture != null)
			{
				UnityEngine.Object.DestroyImmediate(currentTexture);
			}
			currentTexture = new UnityEngine.RenderTexture(desc);
			currentTexture.Create();
		}
		for (int i = 0; i < 6; i++)
		{
			Graphics.CopyTexture(input, i, 0, currentTexture, i, 0);
		}
		return Render_GPU_Project_Uniform_9Coeff(currentTexture, output);
	}

	private static RenderTextureFormat? ConvertRenderFormat(TextureFormat input_format)
	{
		return input_format switch
		{
			TextureFormat.RGBA32 => RenderTextureFormat.ARGB32,
			TextureFormat.RGBAHalf => RenderTextureFormat.ARGBHalf,
			TextureFormat.RGBAFloat => RenderTextureFormat.ARGBFloat,
			_ => null,
		};
	}

	private static bool Render_GPU_Project_Uniform_9Coeff(UnityEngine.RenderTexture input, Vector4[] output)
	{
		if (_compute == null)
		{
			_compute = Resources.Load<ComputeShader>("SphericalHarmonics/SH_Reduce_Uniform");
			_ReduceKernel = _compute.FindKernel("Reduce");
			for (int i = 0; i < 9; i++)
			{
				_SHkernels[i] = _compute.FindKernel("sh_" + i);
			}
		}
		int num = Mathf.CeilToInt((float)input.width / 8f);
		ComputeBuffer computeBuffer = new ComputeBuffer(9, 16);
		ComputeBuffer computeBuffer2 = new ComputeBuffer(num * num * 6, 16);
		ComputeBuffer computeBuffer3 = new ComputeBuffer(num * num * 6, 16);
		for (int j = 0; j < 9; j++)
		{
			num = Mathf.CeilToInt((float)input.width / 8f);
			int kernelIndex = _SHkernels[j];
			_compute.SetInt("coeff", j);
			_compute.SetTexture(kernelIndex, "input_data", input);
			_compute.SetBuffer(kernelIndex, "output_buffer", computeBuffer2);
			_compute.SetBuffer(kernelIndex, "coefficients", computeBuffer);
			_compute.SetInt("ceiled_size", num);
			_compute.SetInt("input_size", input.width);
			_compute.SetInt("row_size", num);
			_compute.SetInt("face_size", num * num);
			_compute.Dispatch(kernelIndex, num, num, 1);
			kernelIndex = _ReduceKernel;
			int num2 = 0;
			_buffers[0] = computeBuffer2;
			_buffers[1] = computeBuffer3;
			while (num > 1)
			{
				_compute.SetInt("input_size", num);
				num = Mathf.CeilToInt((float)num / 8f);
				_compute.SetInt("ceiled_size", num);
				_compute.SetBuffer(kernelIndex, "coefficients", computeBuffer);
				_compute.SetBuffer(kernelIndex, "input_buffer", _buffers[num2]);
				_compute.SetBuffer(kernelIndex, "output_buffer", _buffers[(num2 + 1) % 2]);
				_compute.Dispatch(kernelIndex, num, num, 1);
				num2 = (num2 + 1) % 2;
			}
		}
		computeBuffer.GetData(output);
		computeBuffer3.Release();
		computeBuffer2.Release();
		computeBuffer.Release();
		return true;
	}
}

public class ApplyChangesReflectionProbeSH2Connector : UpdatePacket<ReflectionProbeSH2Connector>
{
	ReflectionProbeSH2Connector.ComputeResult result;
	Vector4[] output;
	float Order0Scale;
	float Order1Scale;
	float Order2Scale;
	FrooxEngine.ReflectionProbe probe;
	public ApplyChangesReflectionProbeSH2Connector(ReflectionProbeSH2Connector owner) : base(owner)
	{
		output = ReflectionProbeSH2Connector.output;
		Order0Scale = Owner.Owner.Order0Scale;
		Order1Scale = Owner.Owner.Order1Scale;
		Order2Scale = Owner.Owner.Order2Scale;
		probe = Owner.Owner.Probe.Target;
	}

	public override void Update()
	{
		result = Owner.ComputeFromProbe(probe, output);
		switch (result)
		{
			case ReflectionProbeSH2Connector.ComputeResult.Computed:
				{
					SphericalHarmonicsL2<colorX> sphericalHarmonicsL = default(SphericalHarmonicsL2<colorX>);
					bool flag = true;
					for (int i = 0; i < 9; i++)
					{
						Vector4 vector = output[i];
						sphericalHarmonicsL[i] = new colorX(vector.x, vector.y, vector.z, 1f, ColorProfile.Linear);
						if (!sphericalHarmonicsL[i].rgb.IsValid())
						{
							flag = false;
							break;
						}
					}
					if (flag)
					{
						sphericalHarmonicsL = sphericalHarmonicsL.ScaleOrders(Order0Scale, Order1Scale, Order2Scale);
						base.Owner.Owner.UpdateValue(sphericalHarmonicsL);
					}
					break;
				}
			case ReflectionProbeSH2Connector.ComputeResult.Failed:
				base.Owner.Owner.UpdateValue(default(SphericalHarmonicsL2<colorX>));
				break;
			case ReflectionProbeSH2Connector.ComputeResult.Postpone:
				base.Owner.Owner.MarkChangeDirty();
				break;
		}
	}
}